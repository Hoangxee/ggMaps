using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace GGMaps
{
    public sealed class SpeedInterval
    {
        public int Start;
        public int End;
        public string Speed;   // NORMAL | SLOW | TRAFFIC_JAM
    }

    public sealed class TransitInfo
    {
        public string LineName;
        public string LineShort;
        public Color LineColor = Color.FromArgb(255, 0x1A, 0x73, 0xE8);
        public Color LineTextColor = Colors.White;
        public string VehicleName;
        public string VehicleType;
        public string Headsign;
        public string DepartureStop;
        public string ArrivalStop;
        public string DepartureTime;
        public string ArrivalTime;
        public int StopCount;

        public string ChipText { get { return !string.IsNullOrEmpty(LineShort) ? LineShort : (LineName ?? VehicleName ?? "?"); } }
    }

    public sealed class RouteStep
    {
        public string Instruction { get; set; }
        public string Maneuver { get; set; }
        public int DistanceMeters { get; set; }
        public int DurationSeconds { get; set; }
        public string DistanceText { get; set; }
        public string DurationText { get; set; }
        public string TravelMode { get; set; }
        public TransitInfo Transit { get; set; }
        public LatLng Start;
        public LatLng End;
        public List<LatLng> Points = new List<LatLng>();
    }

    /// <summary>One route of a Routes API computeRoutes response.</summary>
    public sealed class RouteInfo
    {
        public string TravelMode;
        public int DurationSeconds;
        public int StaticDurationSeconds;
        public int DistanceMeters;
        public string DurationText;
        public string DistanceText;
        public string Description;
        public List<string> Warnings = new List<string>();
        public List<string> Labels = new List<string>();
        public bool HasTolls;
        public string Fare;
        public string TransitInfoText;   // "mỗi 15 phút"
        public string DepartText, ArriveText;
        /// <summary>Computed by OpenStreetMap / Valhalla (motorbike, bicycle) instead of Google.</summary>
        public bool IsOsm;
        public List<LatLng> Points = new List<LatLng>();
        public List<SpeedInterval> Speed = new List<SpeedInterval>();
        public List<RouteStep> Steps = new List<RouteStep>();
        public int Index;

        public bool IsTransit { get { return TravelMode == "TRANSIT"; } }

        /// <summary>Traffic delay vs. free-flow, in seconds (DRIVE / TWO_WHEELER only).</summary>
        public int DelaySeconds { get { return Math.Max(0, DurationSeconds - StaticDurationSeconds); } }

        /// <summary>Duration colour on iOS: green = normal, orange = some delay, red = heavy.</summary>
        public Brush DurationBrush
        {
            get
            {
                if (TravelMode != "DRIVE" && TravelMode != "TWO_WHEELER") return Fmt.OnSurfaceBrush;
                if (StaticDurationSeconds <= 0) return Fmt.GreenBrush;
                double ratio = (double)DurationSeconds / StaticDurationSeconds;
                if (ratio > 1.4) return Fmt.RedBrush;
                if (ratio > 1.15) return Fmt.OrangeBrush;
                return Fmt.GreenBrush;
            }
        }

        public string ViaText { get { return string.IsNullOrEmpty(Description) ? "" : "Qua " + Description; } }

        /// <summary>Second line under the duration, like iOS ("Tuyến nhanh nhất, tình trạng giao thông bình thường").</summary>
        public string Subtitle
        {
            get
            {
                if (IsTransit)
                {
                    var now = DateTime.Now;
                    string s = !string.IsNullOrEmpty(DepartText) && !string.IsNullOrEmpty(ArriveText)
                        ? DepartText + " – " + ArriveText
                        : Fmt.Clock(now) + " – " + Fmt.Clock(now.AddSeconds(DurationSeconds));
                    if (!string.IsNullOrEmpty(TransitInfoText)) s += " · " + TransitInfoText;
                    if (!string.IsNullOrEmpty(Fare)) s += " · " + Fare;
                    return s;
                }
                if (IsOsm)
                {
                    string kind = TravelMode == "BICYCLE" ? "xe đạp" : "xe máy";
                    return (Index == 0 ? "Tuyến " + kind + " tốt nhất" : "Tuyến " + kind + " khác") + " · dữ liệu OpenStreetMap";
                }
                var parts = new List<string>();
                if (Index == 0) parts.Add("Tuyến nhanh nhất");
                if (TravelMode == "DRIVE" || TravelMode == "TWO_WHEELER")
                {
                    int delayMin = DelaySeconds / 60;
                    if (delayMin >= 5) parts.Add("chậm hơn " + delayMin + " phút do giao thông");
                    else if (Index == 0) parts.Add("tình trạng giao thông bình thường");
                }
                if (HasTolls) parts.Add("có trạm thu phí");
                string t = string.Join(", ", parts);
                return t.Length > 0 ? char.ToUpper(t[0]) + t.Substring(1) : "";
            }
        }

        public Visibility SubtitleVisibility { get { return Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ViaVisibility { get { return ViaText.Length > 0 ? Visibility.Visible : Visibility.Collapsed; } }
    }
}

namespace GGMaps
{
    /// <summary>Line chip / walk icon in a transit route summary.</summary>
    public sealed class RouteChip
    {
        public string IconData { get; set; }
        public string ChipText { get; set; }
        public Brush ChipBg { get; set; }
        public Brush ChipFg { get; set; }
        public bool First { get; set; }
        public Visibility ChipVisibility { get { return string.IsNullOrEmpty(ChipText) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility SeparatorVisibility { get { return First ? Visibility.Collapsed : Visibility.Visible; } }
    }

    /// <summary>Row of the route list in the directions sheet.</summary>
    public sealed class RouteRow
    {
        public RouteInfo Route { get; set; }
        public bool Selected { get; set; }
        public List<RouteChip> Chips { get; set; }
        public string DistanceInParens { get { return Route.IsTransit ? "" : "(" + Route.DistanceText + ")"; } }
        public Visibility ChipsVisibility { get { return Chips != null && Chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility SelectedVisibility { get { return Selected ? Visibility.Visible : Visibility.Collapsed; } }
    }

    /// <summary>Row of the step list screen.</summary>
    public sealed class StepItem
    {
        public string IconData { get; set; }
        public Brush IconBrush { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Meta { get; set; }
        public string ChipText { get; set; }
        public Brush ChipBg { get; set; }
        public Brush ChipFg { get; set; }
        public List<LatLng> Points { get; set; }
        public Visibility SubtitleVisibility { get { return string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility MetaVisibility { get { return string.IsNullOrEmpty(Meta) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility ChipVisibility { get { return string.IsNullOrEmpty(ChipText) ? Visibility.Collapsed : Visibility.Visible; } }
    }
}
