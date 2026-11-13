using System;
using System.Globalization;
using Windows.Storage;

namespace GGMaps
{
    /// <summary>Typed wrapper over ApplicationData.LocalSettings. Reads/writes never throw.</summary>
    public static class AppSettings
    {
        private static object Get(string key)
        {
            try
            {
                object v;
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out v)) return v;
            }
            catch { }
            return null;
        }

        private static void Set(string key, object value)
        {
            try
            {
                if (value == null) ApplicationData.Current.LocalSettings.Values.Remove(key);
                else ApplicationData.Current.LocalSettings.Values[key] = value;
            }
            catch { }
        }

        private static string GetString(string key, string def) { var v = Get(key) as string; return v ?? def; }
        private static bool GetBool(string key, bool def) { var v = Get(key); return v is bool ? (bool)v : def; }
        private static double GetDouble(string key, double def) { var v = Get(key); return v is double ? (double)v : def; }

        /// <summary>Google Maps Platform API key (Map Tiles, Places (New), Routes, Geocoding).</summary>
        public static string ApiKey
        {
            get { return (GetString("ApiKey", "") ?? "").Trim(); }
            set { Set("ApiKey", (value ?? "").Trim()); }
        }

        public static bool HasApiKey { get { return ApiKey.Length > 10; } }

        /// <summary>"light" (default, like Google Maps on iOS), "dark" or "system". Applied on next launch.</summary>
        public static string Theme { get { return GetString("Theme", "light"); } set { Set("Theme", value); } }

        /// <summary>"roadmap" | "satellite" | "terrain"</summary>
        public static string MapType { get { return GetString("MapType", "roadmap"); } set { Set("MapType", value); } }

        public static bool Traffic { get { return GetBool("Traffic", false); } set { Set("Traffic", value); } }

        /// <summary>Map label size: "scaleFactor1x" | "scaleFactor2x" (default; WP renders 256 px tiles in physical pixels).</summary>
        public static string LabelScale { get { return GetString("LabelScale", "scaleFactor2x"); } set { Set("LabelScale", value); } }

        public static bool AvoidTolls { get { return GetBool("AvoidTolls", false); } set { Set("AvoidTolls", value); } }
        public static bool AvoidHighways { get { return GetBool("AvoidHighways", false); } set { Set("AvoidHighways", value); } }
        public static bool AvoidFerries { get { return GetBool("AvoidFerries", false); } set { Set("AvoidFerries", value); } }

        /// <summary>DRIVE | TWO_WHEELER | TRANSIT | WALK | BICYCLE</summary>
        public static string TravelMode { get { return GetString("TravelMode", "DRIVE"); } set { Set("TravelMode", value); } }

        public static string Language { get { return GetString("Language", "vi"); } set { Set("Language", value); } }
        public static string Region { get { return GetString("Region", "VN"); } set { Set("Region", value); } }

        /// <summary>Default address used instead of GPS ("my location"); none until the user picks one.</summary>
        public static bool HasMyLocation { get { return Get("MyLat") is double && Get("MyLon") is double; } }
        public static double MyLat { get { return GetDouble("MyLat", double.NaN); } }
        public static double MyLon { get { return GetDouble("MyLon", double.NaN); } }
        public static string MyLabel { get { return GetString("MyLabel", ""); } }

        public static void SetMyLocation(double lat, double lon, string label)
        {
            Set("MyLat", lat);
            Set("MyLon", lon);
            Set("MyLabel", string.IsNullOrWhiteSpace(label) ? "Địa chỉ mặc định" : label.Trim());
        }

        public static void ClearMyLocation()
        {
            Set("MyLat", null);
            Set("MyLon", null);
            Set("MyLabel", null);
        }

        public static bool HasSavedCamera { get { return Get("LastLat") is double; } }
        public static double LastLat { get { return GetDouble("LastLat", 21.0285); } set { Set("LastLat", value); } }
        public static double LastLon { get { return GetDouble("LastLon", 105.8542); } set { Set("LastLon", value); } }
        public static double LastZoom { get { return GetDouble("LastZoom", 13); } set { Set("LastZoom", value); } }

        // ---------------------------------------------------------------
        // Map Tiles API session cache: one entry per session config (key = config hash)
        // ---------------------------------------------------------------

        public static string GetTileSession(string config)
        {
            var raw = GetString("TileSess_" + config, null);
            if (string.IsNullOrEmpty(raw)) return null;
            int bar = raw.IndexOf('|');
            if (bar <= 0) return null;
            long expiry;
            if (!long.TryParse(raw.Substring(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out expiry)) return null;
            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            if (expiry - now < 6 * 3600) return null;   // renew well before expiry
            return raw.Substring(bar + 1);
        }

        public static void SetTileSession(string config, string session, long expiryUnix)
        {
            Set("TileSess_" + config, expiryUnix.ToString(CultureInfo.InvariantCulture) + "|" + session);
        }

        public static void ClearTileSessions()
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                var keys = new System.Collections.Generic.List<string>();
                foreach (var k in values.Keys) if (k.StartsWith("TileSess_")) keys.Add(k);
                foreach (var k in keys) values.Remove(k);
            }
            catch { }
        }
    }
}
