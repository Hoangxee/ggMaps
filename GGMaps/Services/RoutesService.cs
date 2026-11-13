using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Windows.UI;

namespace GGMaps
{
    /// <summary>
    /// Route overview + step list (no real-time navigation).
    ///  * DRIVE / WALK / TRANSIT: Google Maps web endpoint /maps/preview/directions (no key).
    ///  * TWO_WHEELER / BICYCLE: Google Maps web has no motorbike / bicycle routing in Vietnam, so these use BRouter
    ///    (OpenStreetMap data, profiles "moped" / "trekking") over plain http.
    /// </summary>
    public static class RoutesService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(double v) { return v.ToString("0.#######", Inv); }

        public static bool UsesOsm(string mode) { return mode == "TWO_WHEELER" || mode == "BICYCLE"; }

        public static Task<List<RouteInfo>> ComputeAsync(LatLng origin, LatLng dest, string mode, CancellationToken ct)
        {
            if (UsesOsm(mode)) return BRouterAsync(origin, dest, mode, ct);
            return GoogleAsync(origin, dest, mode, ct);
        }

        // =====================================================================
        // Google Maps web directions
        // =====================================================================

        /// <summary>pb copied from the web app; 6m count = number of descendants (grows with the avoid flags).</summary>
        private static string GooglePb(LatLng o, LatLng d, int mode, bool highways, bool tolls, bool ferries)
        {
            string av = (highways ? "!1b1" : "") + (tolls ? "!2b1" : "");
            int a = (highways ? 1 : 0) + (tolls ? 1 : 0);
            int e = highways || tolls || ferries ? 1 : 0;
            int f = ferries ? 1 : 0;
            int n = 61 + a + e + f;
            var c = new LatLng((o.Lat + d.Lat) / 2, (o.Lon + d.Lon) / 2);
            return "!1m4!3m2!3d" + F(o.Lat) + "!4d" + F(o.Lon) + "!6e2!1m4!3m2!3d" + F(d.Lat) + "!4d" + F(d.Lon) + "!6e2" +
                   "!3m12!1m3!1d" + F(Math.Max(2000, LatLng.Distance(o, d) * 1.5)) + "!2d" + F(c.Lon) + "!3d" + F(c.Lat) +
                   "!2m3!1f0.0!2f0.0!3f0.0!3m2!1i1024!2i768!4f13.1" +
                   "!6m" + n + "!1m5!18b1!30b1!31m1!1b1!34e1!2m" + (4 + a) + av + "!5m1!6e2!20e3!39b1!6m" + (30 + e) +
                   "!32i1!49b1!63m0!66b1!85b1!114b1!149b1!206b1!209b1!212b1!215b1!216b1!222b1!223b1!234b1!235b1" + (e == 1 ? "!239b1" : "") +
                   "!246b1!253b1!260b1!262b1!266b1!270b1!271b1!273b1!281b1!294b1!300b1!301b1!302i300!303i100" + (ferries ? "!7b1" : "") +
                   "!10b1!12b1!13b1!14b1!16b1!17m1!3e1!20m5!1e" + mode + "!2e3!5e2!6b1!14b1!46m1!1b0!96b1!97m1!2b1!99b1" +
                   "!15m2!7e81!15i10142!20m28!1m6!1m2!1i0!2i0!2m2!1i530!2i768!1m6!1m2!1i974!2i0!2m2!1i1024!2i768!1m6!1m2!1i0!2i0" +
                   "!2m2!1i1024!2i20!1m6!1m2!1i0!2i748!2m2!1i1024!2i768!27b1!40i803!47m2!8b1!10e2";
        }

        private static async Task<List<RouteInfo>> GoogleAsync(LatLng o, LatLng dst, string mode, CancellationToken ct)
        {
            int m = mode == "WALK" ? 2 : mode == "TRANSIT" ? 3 : 0;
            bool road = mode == "DRIVE";
            string pb = GooglePb(o, dst, m, road && AppSettings.AvoidHighways, road && AppSettings.AvoidTolls, road && AppSettings.AvoidFerries);
            string url = "https://www.google.com/maps/preview/directions?authuser=0&hl=" + AppSettings.Language +
                         "&gl=" + AppSettings.Region.ToLowerInvariant() + "&pb=" + Uri.EscapeDataString(pb);
            var d = await MapsApi.GetJsonAsync(url, ct);

            var list = new List<RouteInfo>();
            var routes = MapsApi.Arr(d, 0, 1);
            if (routes == null) return list;
            for (int i = 0; i < routes.Count; i++)
            {
                var info = ParseGoogleRoute(routes[i], mode);
                info.Points = DecodeDelta(MapsApi.Arr(d, 0, 7, i, 0), MapsApi.Arr(d, 0, 7, i, 1));
                info.Index = list.Count;
                AttachStepPoints(info);
                list.Add(info);
            }
            return list;
        }

        /// <summary>d[0][7][i] = [lat deltas, lng deltas] in 1e-7 degrees, first value absolute.</summary>
        private static List<LatLng> DecodeDelta(JArray lats, JArray lngs)
        {
            var pts = new List<LatLng>();
            if (lats == null || lngs == null) return pts;
            long la = 0, lo = 0;
            int n = Math.Min(lats.Count, lngs.Count);
            for (int i = 0; i < n; i++)
            {
                la += (long)lats[i];
                lo += (long)lngs[i];
                pts.Add(new LatLng(la / 1e7, lo / 1e7));
            }
            return pts;
        }

        private static int Sec(JToken t, params int[] path)
        {
            double v = MapsApi.Num(t, path);
            return double.IsNaN(v) ? 0 : (int)v;
        }

        private static RouteInfo ParseGoogleRoute(JToken r, string mode)
        {
            var s = MapsApi.At(r, 0);
            var info = new RouteInfo { TravelMode = mode };
            info.DistanceMeters = Sec(s, 2, 0);
            info.DistanceText = Fmt.Distance(info.DistanceMeters);
            info.StaticDurationSeconds = Sec(s, 3, 0);
            int traffic = Sec(s, 10, 0, 0);
            info.DurationSeconds = traffic > 0 ? traffic : info.StaticDurationSeconds;
            info.DurationText = Fmt.Duration(info.DurationSeconds);

            if (mode == "TRANSIT")
            {
                info.TransitInfoText = MapsApi.Str(s, 1);   // "mỗi 15 phút"
                info.DepartText = MapsApi.Str(s, 5, 0, 2);
                info.ArriveText = MapsApi.Str(s, 5, 1, 2);
                info.Fare = MapsApi.Str(s, 11, 1);
            }
            else
            {
                info.Description = MapsApi.Str(s, 1);
            }

            // steps: r[1][0][1] = groups; non-transit groups list their steps in g[1][j][0]; transit groups are one ride
            var groups = MapsApi.Arr(r, 1, 0, 1);
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    var head = MapsApi.At(g, 0);
                    bool ride = Sec(head, 0) == 3 || (mode == "TRANSIT" && MapsApi.At(g, 5, 0) != null && MapsApi.Arr(g, 1) == null);
                    if (ride)
                    {
                        info.Steps.Add(ParseRide(g));
                        continue;
                    }
                    var steps = MapsApi.Arr(g, 1);
                    if (steps == null) continue;
                    foreach (var st in steps)
                    {
                        var step = ParseStep(MapsApi.At(st, 0), mode == "TRANSIT" ? "WALK" : mode);
                        if (step != null) info.Steps.Add(step);
                    }
                }
            }
            return info;
        }

        private static readonly Regex ManeuverRx = new Regex("maneuver='([A-Z_]+)'");
        private static readonly Regex SideRx = new Regex("side='([A-Z]+)'");
        private static readonly Regex TypeRx = new Regex("type='([A-Z_]+)'");
        private static readonly Regex TagRx = new Regex("<[^>]+>");

        private static RouteStep ParseStep(JToken st, string mode)
        {
            if (st == null) return null;
            var step = new RouteStep { TravelMode = mode };
            step.DistanceMeters = Sec(st, 2, 0);
            step.DurationSeconds = Sec(st, 3, 0);
            step.DistanceText = Fmt.Distance(step.DistanceMeters);
            step.DurationText = Fmt.Duration(step.DurationSeconds);

            string html = MapsApi.Str(st, 1) ?? "";
            var parts = MapsApi.Arr(st, 14);
            if (parts != null)
            {
                var sb = new StringBuilder();
                foreach (var part in parts) sb.Append(MapsApi.Str(part, 1, 0));
                step.Instruction = sb.ToString().Trim();
            }
            if (string.IsNullOrEmpty(step.Instruction)) step.Instruction = System.Net.WebUtility.HtmlDecode(TagRx.Replace(html, "")).Trim();
            step.Maneuver = MapManeuver(html);

            double lat = MapsApi.Num(st, 7, 2, 2), lng = MapsApi.Num(st, 7, 2, 3);
            if (!double.IsNaN(lat) && !double.IsNaN(lng)) step.Start = new LatLng(lat, lng);
            return step;
        }

        /// <summary>Google's &lt;step maneuver='TURN'&gt;&lt;turn side='LEFT' type='KEEP'&gt; -&gt; Routes-API-style names used by ManeuverIcons.</summary>
        private static string MapManeuver(string html)
        {
            var m = ManeuverRx.Match(html);
            string man = m.Success ? m.Groups[1].Value : "";
            var sm = SideRx.Match(html);
            string side = sm.Success ? sm.Groups[1].Value : "";
            var tm = TypeRx.Match(html);
            string type = tm.Success ? tm.Groups[1].Value : "";
            string dir = side == "LEFT" ? "LEFT" : side == "RIGHT" ? "RIGHT" : "";
            switch (man)
            {
                case "TURN":
                    if (dir == "") return "STRAIGHT";
                    if (type == "KEEP") return "FORK_" + dir;
                    if (type == "SLIGHT") return "TURN_SLIGHT_" + dir;
                    if (type == "SHARP") return "TURN_SHARP_" + dir;
                    return "TURN_" + dir;
                case "UTURN": return "UTURN_LEFT";
                case "ROUNDABOUT":
                case "ROUNDABOUT_EXIT": return "ROUNDABOUT_" + (dir == "" ? "RIGHT" : dir);
                case "MERGE": return "MERGE";
                case "FERRY": return "FERRY";
                case "RAMP":
                case "EXIT": return "RAMP_" + (dir == "" ? "RIGHT" : dir);
                case "DEPART": return "DEPART";
                default: return "STRAIGHT";
            }
        }

        private static readonly Color BusChip = Color.FromArgb(255, 0x1A, 0x73, 0xE8);

        private static RouteStep ParseRide(JToken g)
        {
            var head = MapsApi.At(g, 0);
            var step = new RouteStep { TravelMode = "TRANSIT" };
            step.DurationSeconds = Sec(head, 3, 0);
            step.DurationText = Fmt.Duration(step.DurationSeconds);
            var t = new TransitInfo { LineColor = BusChip, LineTextColor = Colors.White };
            var line = MapsApi.Arr(head, 14);
            if (line != null)
            {
                foreach (var item in line)
                {
                    int kind = Sec(item, 0);
                    if (kind == 4) t.VehicleName = MapsApi.Str(item, 2, 3);            // "Xe buýt"
                    else if (kind == 5) t.LineShort = MapsApi.Str(item, 1, 0);         // "E11"
                    else if (kind == 7) t.Headsign = MapsApi.Str(item, 1, 0);          // direction
                }
            }
            string icon = MapsApi.Str(head, 14, 0, 2, 1) ?? "";
            t.VehicleType = icon.Contains("rail") || icon.Contains("train") || icon.Contains("subway") || icon.Contains("metro") ? "RAIL" : "BUS";
            t.DepartureStop = MapsApi.Str(g, 5, 0, 0);
            t.DepartureTime = MapsApi.Str(g, 5, 0, 3, 2);
            t.ArrivalStop = MapsApi.Str(g, 5, 1, 0);
            t.ArrivalTime = MapsApi.Str(g, 5, 1, 2, 2);
            double stops = MapsApi.Num(g, 5, 2);
            t.StopCount = double.IsNaN(stops) ? 0 : (int)stops;
            double la = MapsApi.Num(g, 5, 0, 4, 2), lo = MapsApi.Num(g, 5, 0, 4, 3);
            if (!double.IsNaN(la) && !double.IsNaN(lo)) step.Start = new LatLng(la, lo);
            double la2 = MapsApi.Num(g, 5, 1, 4, 2), lo2 = MapsApi.Num(g, 5, 1, 4, 3);
            if (!double.IsNaN(la2) && !double.IsNaN(lo2)) step.End = new LatLng(la2, lo2);
            step.Instruction = (t.VehicleName ?? "Xe buýt") + " " + t.ChipText;
            step.Transit = t;
            return step;
        }

        /// <summary>Cuts the route polyline into per-step pieces using each step's start point (nearest polyline vertex).</summary>
        private static void AttachStepPoints(RouteInfo info)
        {
            var pts = info.Points;
            if (pts.Count < 2 || info.Steps.Count == 0) return;
            int prev = 0;
            var starts = new List<int>();
            foreach (var s in info.Steps)
            {
                int idx = prev;
                if (!s.Start.IsZero)
                {
                    double best = double.MaxValue;
                    for (int i = prev; i < pts.Count; i++)
                    {
                        double dd = LatLng.Distance(pts[i], s.Start);
                        if (dd < best) { best = dd; idx = i; }
                    }
                }
                starts.Add(idx);
                prev = idx;
            }
            for (int k = 0; k < info.Steps.Count; k++)
            {
                int a = starts[k];
                int b = k + 1 < starts.Count ? Math.Max(a, starts[k + 1]) : pts.Count - 1;
                if (!info.Steps[k].End.IsZero && k + 1 >= starts.Count) b = pts.Count - 1;
                info.Steps[k].Points = pts.GetRange(a, Math.Max(1, b - a + 1));
            }
        }

        // =====================================================================
        // BRouter (OpenStreetMap) for motorbike / bicycle
        //   brouter.de answers over plain http (Valhalla's public server is TLS 1.3 only, which Windows Phone 8.1
        //   cannot speak). Profile "moped" = small motorbike: no motorways, small streets allowed.
        //   BRouter data has no street names, so instructions are "Rẽ trái / Vòng xuyến lối ra thứ 2" + distance.
        // =====================================================================

        private const string BRouterUrl = "http://brouter.de/brouter";

        private static async Task<List<RouteInfo>> BRouterAsync(LatLng o, LatLng d, string mode, CancellationToken ct)
        {
            if (MapsApi.IsOffline) throw new MapsApiException(0, MapsApi.OfflineMessage);
            string profile = mode == "BICYCLE" ? "trekking" : "moped";
            var tasks = new List<Task<JObject>>();
            for (int alt = 0; alt < 2; alt++) tasks.Add(BRouterOneAsync(o, d, profile, alt, ct));
            var list = new List<RouteInfo>();
            for (int i = 0; i < tasks.Count; i++)
            {
                JObject json;
                try { json = await tasks[i]; }
                catch (Exception ex)
                {
                    if (i == 0) throw;
                    DiagLog.Write("brouter alt failed " + ex.Message);
                    continue;
                }
                if (json == null) continue;
                var info = ParseBRouter(json, mode);
                if (info == null) continue;
                // alternatives sometimes repeat the main route
                bool dup = list.Exists(r => Math.Abs(r.DistanceMeters - info.DistanceMeters) < 30);
                if (dup) continue;
                info.Index = list.Count;
                list.Add(info);
            }
            return list;
        }

        private static async Task<JObject> BRouterOneAsync(LatLng o, LatLng d, string profile, int alt, CancellationToken ct)
        {
            string url = BRouterUrl + "?lonlats=" + F(o.Lon) + "," + F(o.Lat) + "%7C" + F(d.Lon) + "," + F(d.Lat) +
                         "&profile=" + profile + "&alternativeidx=" + alt + "&format=geojson";
            using (var resp = await MapsApi.Http.GetAsync(url, ct))
            {
                string text = await resp.Content.ReadAsStringAsync();
                int status = (int)resp.StatusCode;
                if (status == 200 && text.TrimStart().StartsWith("{")) return JObject.Parse(text);
                DiagLog.Write("brouter " + status + " " + (text.Length > 200 ? text.Substring(0, 200) : text));
                if (MapsApi.IsOffline) throw new MapsApiException(0, MapsApi.OfflineMessage);
                // BRouter answers 400/500 with a plain-text reason when there is no route
                if (text.Contains("not found") || text.Contains("no track") || text.Contains("operation killed")) return null;
                throw new MapsApiException(status, "Máy chủ chỉ đường OpenStreetMap (BRouter) báo lỗi: " + (text.Length > 120 ? text.Substring(0, 120) : text));
            }
        }

        private static RouteInfo ParseBRouter(JObject json, string mode)
        {
            var f = json["features"] != null ? json["features"][0] : null;
            if (f == null) return null;
            var props = f["properties"];
            var coords = f["geometry"] != null ? f["geometry"]["coordinates"] as JArray : null;
            if (props == null || coords == null || coords.Count < 2) return null;

            var info = new RouteInfo { TravelMode = mode, IsOsm = true };
            foreach (var c in coords) info.Points.Add(new LatLng((double)c[1], (double)c[0]));
            info.DistanceMeters = (int)ParseD(props["track-length"]);
            info.DurationSeconds = (int)ParseD(props["total-time"]);
            info.StaticDurationSeconds = info.DurationSeconds;
            info.DistanceText = Fmt.Distance(info.DistanceMeters);
            info.DurationText = Fmt.Duration(info.DurationSeconds);

            // cumulative time per point (seconds) -> step durations
            var times = ParseNumberArray(props["times"]);
            // voicehints: [pointIndex, command, roundaboutExit, distanceToNextHint, angle]
            var hints = new List<double[]>();
            var vhToken = props["voicehints"];
            JArray vh = vhToken as JArray;
            if (vh == null && vhToken != null && vhToken.Type == JTokenType.String) vh = JArray.Parse((string)vhToken);
            if (vh != null)
            {
                foreach (var h in vh)
                {
                    var arr = h as JArray;
                    if (arr == null || arr.Count < 4) continue;
                    hints.Add(new[] { (double)arr[0], (double)arr[1], (double)arr[2], (double)arr[3] });
                }
            }

            int n = info.Points.Count;
            int firstHint = hints.Count > 0 ? (int)hints[0][0] : n - 1;
            AddOsmStep(info, "Xuất phát", "DEPART", 0, Math.Min(firstHint, n - 1), times);
            for (int i = 0; i < hints.Count; i++)
            {
                int a = Math.Max(0, Math.Min((int)hints[i][0], n - 1));
                int b = i + 1 < hints.Count ? Math.Max(a, Math.Min((int)hints[i + 1][0], n - 1)) : n - 1;
                string man;
                string text = HintText((int)hints[i][1], (int)hints[i][2], out man);
                AddOsmStep(info, text, man, a, b, times);
            }
            var end = new RouteStep { TravelMode = mode, Instruction = "Đến nơi", Maneuver = "DESTINATION", DistanceText = "", DurationText = "" };
            end.Points = new List<LatLng> { info.Points[n - 1] };
            info.Steps.Add(end);
            return info;
        }

        private static void AddOsmStep(RouteInfo info, string text, string maneuver, int a, int b, List<double> times)
        {
            var pts = info.Points.GetRange(a, b - a + 1);
            double dist = 0;
            for (int i = 1; i < pts.Count; i++) dist += LatLng.Distance(pts[i - 1], pts[i]);
            int dur = times.Count > b ? (int)(times[b] - times[a]) : 0;
            var step = new RouteStep
            {
                TravelMode = info.TravelMode,
                Instruction = text + (dist >= 20 ? " rồi đi tiếp " + Fmt.Distance(dist) : ""),
                Maneuver = maneuver,
                DistanceMeters = (int)dist,
                DurationSeconds = dur,
                DistanceText = Fmt.Distance(dist),
                DurationText = Fmt.Duration(dur),
                Points = pts
            };
            info.Steps.Add(step);
        }

        /// <summary>BRouter VoiceHint command -> Vietnamese text + Routes-style maneuver name.</summary>
        private static string HintText(int cmd, int exit, out string maneuver)
        {
            switch (cmd)
            {
                case 2: maneuver = "TURN_LEFT"; return "Rẽ trái";
                case 3: maneuver = "TURN_SLIGHT_LEFT"; return "Chếch sang trái";
                case 4: maneuver = "TURN_SHARP_LEFT"; return "Rẽ gắt sang trái";
                case 5: maneuver = "TURN_RIGHT"; return "Rẽ phải";
                case 6: maneuver = "TURN_SLIGHT_RIGHT"; return "Chếch sang phải";
                case 7: maneuver = "TURN_SHARP_RIGHT"; return "Rẽ gắt sang phải";
                case 8: maneuver = "FORK_LEFT"; return "Đi về bên trái";
                case 9: maneuver = "FORK_RIGHT"; return "Đi về bên phải";
                case 10: case 11: case 12: maneuver = "UTURN_LEFT"; return "Quay đầu";
                case 13: case 14:
                    maneuver = "ROUNDABOUT_RIGHT";
                    return exit > 0 ? "Tại vòng xuyến, đi theo lối ra thứ " + exit : "Đi qua vòng xuyến";
                default: maneuver = "STRAIGHT"; return "Đi thẳng";
            }
        }

        /// <summary>BRouter sends numbers either as JSON numbers or as strings ("15016").</summary>
        private static double ParseD(JToken t)
        {
            if (t == null) return 0;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return (double)t;
            double v;
            return double.TryParse(t.ToString(), NumberStyles.Float, Inv, out v) ? v : 0;
        }

        /// <summary>Array of numbers, given as a JSON array or as a string "[0, 2.5, ...]".</summary>
        private static List<double> ParseNumberArray(JToken t)
        {
            var list = new List<double>();
            if (t == null) return list;
            var arr = t as JArray;
            if (arr != null)
            {
                foreach (var x in arr) list.Add(ParseD(x));
                return list;
            }
            foreach (var part in t.ToString().Trim('[', ']', ' ').Split(','))
            {
                double v;
                if (double.TryParse(part.Trim(), NumberStyles.Float, Inv, out v)) list.Add(v);
            }
            return list;
        }
    }
}
