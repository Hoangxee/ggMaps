using System;
using System.Globalization;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace GGMaps
{
    /// <summary>Vietnamese formatting helpers + theme-dependent status colours (call Init once at startup).</summary>
    public static class Fmt
    {
        public static readonly CultureInfo Vi = new CultureInfo("vi-VN");

        public static bool IsDark { get; private set; }
        public static Brush GreenBrush { get; private set; }
        public static Brush RedBrush { get; private set; }
        public static Brush OrangeBrush { get; private set; }
        public static Brush OnSurfaceBrush { get; private set; }
        public static Color RouteBlue { get; private set; }
        public static Color RouteAltColor { get; private set; }
        public static Color RouteBorder { get; private set; }

        public static void Init(bool dark)
        {
            IsDark = dark;
            GreenBrush = new SolidColorBrush(dark ? Color.FromArgb(255, 0x81, 0xC9, 0x95) : Color.FromArgb(255, 0x18, 0x80, 0x38));
            RedBrush = new SolidColorBrush(dark ? Color.FromArgb(255, 0xF2, 0x8B, 0x82) : Color.FromArgb(255, 0xD9, 0x30, 0x25));
            OrangeBrush = new SolidColorBrush(dark ? Color.FromArgb(255, 0xFD, 0xD6, 0x63) : Color.FromArgb(255, 0xE3, 0x74, 0x00));
            OnSurfaceBrush = new SolidColorBrush(dark ? Color.FromArgb(255, 0xE3, 0xE3, 0xE3) : Color.FromArgb(255, 0x1F, 0x1F, 0x1F));
            RouteBlue = dark ? Color.FromArgb(255, 0x66, 0x9D, 0xF6) : Color.FromArgb(255, 0x1A, 0x73, 0xE8);
            RouteAltColor = dark ? Color.FromArgb(255, 0x7A, 0x86, 0x96) : Color.FromArgb(255, 0xA8, 0xB8, 0xD0);
            RouteBorder = dark ? Color.FromArgb(255, 0x0D, 0x3B, 0x8C) : Color.FromArgb(255, 0x10, 0x4D, 0xB0);
        }

        /// <summary>"350 m", "1,2 km", "12 km"</summary>
        public static string Distance(double meters)
        {
            if (double.IsNaN(meters)) return "";
            if (meters < 1000) return ((int)(Math.Round(meters / 10) * 10)).ToString(Vi) + " m";
            if (meters < 10000) return (meters / 1000).ToString("0.#", Vi) + " km";
            return Math.Round(meters / 1000).ToString("#,0", Vi) + " km";
        }

        /// <summary>"5 phút", "1 giờ 20 phút", "2 ngày 3 giờ"</summary>
        public static string Duration(int seconds)
        {
            int min = (int)Math.Round(seconds / 60.0);
            if (min < 1) return "1 phút";
            if (min < 60) return min + " phút";
            int h = min / 60, m = min % 60;
            if (h < 24) return m == 0 ? h + " giờ" : h + " giờ " + m + " phút";
            int d = h / 24; h %= 24;
            return h == 0 ? d + " ngày" : d + " ngày " + h + " giờ";
        }

        public static string Clock(DateTime t) { return t.ToString("HH:mm", CultureInfo.InvariantCulture); }

        /// <summary>"22:00" today, "08:00 Th 3" on another day.</summary>
        public static string TimeWithDay(DateTime t)
        {
            string s = Clock(t);
            if (t.Date == DateTime.Now.Date) return s;
            if (t.Date == DateTime.Now.Date.AddDays(1)) return s + " ngày mai";
            return s + " " + DayShort(t.DayOfWeek);
        }

        public static string DayShort(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "Th 2";
                case DayOfWeek.Tuesday: return "Th 3";
                case DayOfWeek.Wednesday: return "Th 4";
                case DayOfWeek.Thursday: return "Th 5";
                case DayOfWeek.Friday: return "Th 6";
                case DayOfWeek.Saturday: return "Th 7";
                default: return "CN";
            }
        }

        /// <summary>Five stars, rounded to the nearest whole star.</summary>
        public static string Stars(double rating)
        {
            int full = (int)Math.Round(rating);
            if (full < 0) full = 0;
            if (full > 5) full = 5;
            return new string('★', full) + new string('☆', 5 - full);
        }

        /// <summary>"#RRGGBB" -> Color (fallback when invalid).</summary>
        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    return Color.FromArgb(255,
                        byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                        byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
                }
            }
            catch { }
            return fallback;
        }
    }
}
