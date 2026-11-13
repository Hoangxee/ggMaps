using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GGMaps
{
    /// <summary>
    /// Search / autocomplete / place details / reverse geocoding through the endpoints of the Google Maps web app
    /// (no API key): /search?tbm=map, /s?tbm=map&amp;suggest=p, /maps/preview/reveal. The "pb" parameter is the
    /// protobuf-in-URL request copied from the web app, with the viewport substituted.
    /// </summary>
    public static class PlacesService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string F(double v) { return v.ToString("0.#######", Inv); }

        private const string SearchPbTail =
            "!7i20{OFFSET}!10b1!12m57!1m5!18b1!30b1!31m1!1b1!34e1!2m4!5m1!6e2!20e3!39b1!6m28!32i1!49b1!63m0!66b1!85b1!114b1!149b1" +
            "!206b1!209b1!212b1!215b1!216b1!222b1!223b1!234b1!235b1!246b1!253b1!260b1!262b1!266b1!270b1!271b1!273b1!281b1!294b1" +
            "!302i300!303i100!10b1!12b1!13b1!14b1!16b1!17m1!3e1!20m3!5e2!6b1!14b1!46m1!1b0!96b1!97m1!2b1!99b1!19m4!2m3!1i360!2i120" +
            "!4i8!20m57!2m2!1i203!2i100!3m2!2i4!5b1!6m6!1m2!1i86!2i86!1m2!1i408!2i240!7m33!1m3!1e1!2b0!3e3!1m3!1e2!2b1!3e2!1m3!1e2" +
            "!2b0!3e3!1m3!1e8!2b0!3e3!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e10!2b0!3e4!1m3!1e9!2b1!3e2!2b1!9b0!15m8!1m7!1m2!1m1" +
            "!1e2!2m2!1i195!2i195!3i20!22m1!7e81!24m105!1m25!13m9!2b1!3b1!4b1!6i1!8b1!9b1!14b1!20b1!25b1!18m14!3b1!4b1!5b1!6b1" +
            "!13b1!14b1!17b1!21b1!22b1!32b1!33m1!1b1!34b1!36e2!10m1!8e3!11m1!3e1!17b1!20m2!1e3!1e6!24b1!25b1!26b1!27b1!29b1!30m1" +
            "!2b1!36b1!37b1!39m3!2m2!2i1!3i1!43b1!52b1!55b1!56m1!1b1!61m2!1m1!1e1!65m5!3m4!1m3!1m2!1i224!2i298!72m22!1m8!2b1!5b1" +
            "!7b1!12m4!1b1!2b1!4m1!1e1!4b1!8m10!1m6!4m1!1e1!4m1!1e3!4m1!1e4!3sother_user_google_review_posts__and__hotel_and_vr_partner_review_posts" +
            "!6m1!1e1!9b1!89b1!90m2!1m1!1e2!98m3!1b1!2b1!3b1!103b1!113b1!114m3!1b1!2m1!1b1!117b1!122m1!1b1!126b1!127b1!128m1!1b1" +
            "!26m4!2m3!1i80!2i92!4i8!30m28!1m6!1m2!1i0!2i0!2m2!1i530!2i768!1m6!1m2!1i974!2i0!2m2!1i1024!2i768!1m6!1m2!1i0!2i0!2m2" +
            "!1i1024!2i20!1m6!1m2!1i0!2i748!2m2!1i1024!2i768!34m19!2b1!3b1!4b1!6b1!8m6!1b1!3b1!4b1!5b1!6b1!7b1!9b1!12b1!14b1!20b1" +
            "!23b1!25b1!26b1!31b1!37m1!1e81!42b1!49m10!3b1!6m2!1b1!2b1!7m2!1e3!2b1!8b1!9b1!10e2!50m3!2e2!3m1!3b1!61b1!67m5!7b1" +
            "!10b1!14b1!15m1!1b0!69i803!77b1";

        private const string AutocompletePbTail =
            "!7i20!10b1!12m16!1m1!18b1!2m3!5m1!6e2!20e3!10b1!12b1!13b1!16b1!17m1!3e1!20m3!5e2!6b1!14b1!19m4!2m3!1i360!2i120!4i8" +
            "!20m57!2m2!1i203!2i100!3m2!2i4!5b1!6m6!1m2!1i86!2i86!1m2!1i408!2i240!7m42!1m3!1e1!2b0!3e3!1m3!1e2!2b1!3e2!1m3!1e2" +
            "!2b0!3e3!1m3!1e8!2b0!3e3!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e9!2b1!3e2!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e10" +
            "!2b0!3e4!2b1!4b1!9b0!22m3!1s!7e81!17s!23m2!4b1!10b1!24m1!2b1";

        /// <summary>Viewport block: distance across the view in metres, centre, screen size.</summary>
        private static string Viewport(LatLng center, double spanMeters)
        {
            return "!1m3!1d" + F(Math.Max(300, spanMeters)) + "!2d" + F(center.Lon) + "!3d" + F(center.Lat) +
                   "!3m2!1i480!2i800!4f13.1";
        }

        private static string Lang { get { return "&authuser=0&hl=" + AppSettings.Language + "&gl=" + AppSettings.Region.ToLowerInvariant(); } }

        public static string NewSessionToken() { return Guid.NewGuid().ToString(); }

        // ---------------------------------------------------------------- autocomplete

        public static async Task<List<Suggestion>> AutocompleteAsync(string input, LatLng center, double spanMeters, LatLng? origin, CancellationToken ct)
        {
            string pb = "!2i8!4m12" + Viewport(center, spanMeters).Replace("!3m2", "!2m3!1f0!2f0!3f0!3m2") + AutocompletePbTail;
            string url = "https://www.google.com/s?tbm=map&gs_ri=maps&suggest=p" + Lang +
                         "&q=" + Uri.EscapeDataString(input) + "&pb=" + Uri.EscapeDataString(pb);
            var d = await MapsApi.GetJsonAsync(url, ct);
            var list = new List<Suggestion>();
            var rows = MapsApi.Arr(d, 0, 1);
            if (rows == null) return list;
            foreach (var row in rows)
            {
                var x = MapsApi.At(row, 22);
                if (x == null)
                {
                    string q = MapsApi.Str(row, 0);
                    if (!string.IsNullOrEmpty(q)) list.Add(new Suggestion { Kind = SuggestionKind.Query, MainText = q, FullText = q });
                    continue;
                }
                string full = MapsApi.Str(x, 0, 0);
                string main = MapsApi.Str(x, 1, 0) ?? full;
                string secondary = MapsApi.Str(x, 2, 0);
                double lat = MapsApi.Num(x, 11, 2), lng = MapsApi.Num(x, 11, 3);
                var s = new Suggestion { MainText = main, SecondaryText = secondary, FullText = full };
                if (!double.IsNaN(lat) && !double.IsNaN(lng))
                {
                    s.Kind = SuggestionKind.Place;
                    s.Location = new LatLng(lat, lng);
                    s.PlaceId = full;   // no stable id here: the full text is used to fetch details
                    if (origin.HasValue) s.DistanceText = Fmt.Distance(LatLng.Distance(origin.Value, s.Location));
                }
                else
                {
                    s.Kind = SuggestionKind.Query;
                }
                list.Add(s);
            }
            return list;
        }

        // ---------------------------------------------------------------- search

        public sealed class SearchResult
        {
            public List<Place> Places = new List<Place>();
            /// <summary>The query matched one specific place (address, landmark): show its sheet directly.</summary>
            public bool Single;
        }

        public static async Task<SearchResult> SearchAsync(string query, LatLng center, double spanMeters, int offset, CancellationToken ct)
        {
            string pb = "!4m8" + Viewport(center, spanMeters) + SearchPbTail.Replace("{OFFSET}", offset > 0 ? "!8i" + offset : "");
            string url = "https://www.google.com/search?tbm=map" + Lang + "&q=" + Uri.EscapeDataString(query) + "&pb=" + Uri.EscapeDataString(pb);
            var d = await MapsApi.GetJsonAsync(url, ct);
            var result = new SearchResult();

            var list = MapsApi.Arr(d, 64);
            if (list != null && list.Count > 0)
            {
                foreach (var e in list)
                {
                    var p = MapsApi.At(e, 1);
                    if (p == null) continue;
                    var place = Place.FromGoogle(p);
                    if (!string.IsNullOrEmpty(place.Name) && !place.Location.IsZero) result.Places.Add(place);
                }
                return result;
            }

            var single = MapsApi.At(d, 0, 1, 0, 14);
            if (single != null)
            {
                var place = Place.FromGoogle(single);
                if (!place.Location.IsZero)
                {
                    result.Places.Add(place);
                    result.Single = true;
                }
            }
            return result;
        }

        /// <summary>Full details (reviews, description, hours, all photos): searches "name, address" which returns that single place.</summary>
        public static async Task<Place> GetDetailsAsync(Place p, CancellationToken ct)
        {
            string q = string.IsNullOrEmpty(p.Address) ? p.Name : (p.Address.StartsWith(p.Name ?? "\u0000") ? p.Address : p.Name + ", " + p.Address);
            var center = p.Location.IsZero ? new LatLng(AppSettings.LastLat, AppSettings.LastLon) : p.Location;
            var r = await SearchAsync(q, center, 2000, 0, ct);
            if (r.Places.Count == 0) return null;
            if (r.Single) return r.Places[0];
            // several hits: the same place id, else the nearest one
            Place best = null;
            double bestD = double.MaxValue;
            foreach (var c in r.Places)
            {
                if (!string.IsNullOrEmpty(p.Id) && c.Id == p.Id) return c;
                double dd = p.Location.IsZero ? 0 : LatLng.Distance(p.Location, c.Location);
                if (dd < bestD) { bestD = dd; best = c; }
            }
            return bestD < 300 ? best : null;
        }

        /// <summary>Photo URL for the viewer.</summary>
        public static string PhotoUrl(string baseUrl, int maxWidth, int maxHeight)
        {
            return Place.SizedPhoto(baseUrl, maxWidth > 0 ? maxWidth : 960, maxHeight > 0 ? maxHeight : 960);
        }

        // ---------------------------------------------------------------- approximate location

        private static readonly System.Text.RegularExpressions.Regex InitViewRx =
            new System.Text.RegularExpressions.Regex(@"APP_INITIALIZATION_STATE=\[\[\[([0-9.]+),(-?[0-9.]+),(-?[0-9.]+)\]");

        /// <summary>
        /// City-level position guessed by Google from the IP address: google.com/maps without coordinates opens on the
        /// user's area. Used only to bias search while GPS has no fix (never as a route origin).
        /// </summary>
        public static async Task<LatLng?> ApproxLocationAsync(CancellationToken ct)
        {
            if (MapsApi.IsOffline) return null;
            using (var resp = await MapsApi.Http.GetAsync("https://www.google.com/maps?hl=" + AppSettings.Language + "&gl=" + AppSettings.Region.ToLowerInvariant(), ct))
            {
                if (!resp.IsSuccessStatusCode) return null;
                string html = await resp.Content.ReadAsStringAsync();
                var m = InitViewRx.Match(html);
                if (!m.Success) return null;
                double lng, lat;
                if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, Inv, out lng) ||
                    !double.TryParse(m.Groups[3].Value, NumberStyles.Float, Inv, out lat)) return null;
                return new LatLng(lat, lng);
            }
        }

        // ---------------------------------------------------------------- reverse geocoding (dropped pin)

        public static async Task<Place> ReverseGeocodeAsync(LatLng p, CancellationToken ct)
        {
            string pb = "!2m12!1m3!1d200!2d" + F(p.Lon) + "!3d" + F(p.Lat) + "!2m3!1f0!2f0!3f0!3m2!1i480!2i800!4f18" +
                        "!3m2!2d" + F(p.Lon) + "!3d" + F(p.Lat) + "!4m2!1s!7e81!5m5!2m4!1i96!2i64!3i1!4i8";
            string url = "https://www.google.com/maps/preview/reveal?" + Lang.Substring(1) + "&pb=" + Uri.EscapeDataString(pb);
            var d = await MapsApi.GetJsonAsync(url, ct);
            var place = new Place { IsDroppedPin = true, Location = p, Name = "Ghim đã thả" };
            place.Address = MapsApi.Str(d, 1, 0);
            string area = MapsApi.Str(d, 0, 0);
            if (string.IsNullOrEmpty(place.Address) && !string.IsNullOrEmpty(area))
                place.Address = area + (MapsApi.Str(d, 0, 1) != null ? ", " + MapsApi.Str(d, 0, 1) : "");
            return place;
        }
    }
}
