using System;
using System.Globalization;
using Windows.Devices.Geolocation;

namespace GGMaps
{
    /// <summary>Plain latitude / longitude pair (WGS84).</summary>
    public struct LatLng
    {
        public double Lat;
        public double Lon;

        public LatLng(double lat, double lon) { Lat = lat; Lon = lon; }

        public Geopoint ToGeopoint()
        {
            return new Geopoint(new BasicGeoposition { Latitude = Lat, Longitude = Lon });
        }

        public static LatLng From(Geopoint p)
        {
            return new LatLng(p.Position.Latitude, p.Position.Longitude);
        }

        public bool IsZero { get { return Lat == 0 && Lon == 0; } }

        public override string ToString()
        {
            return Lat.ToString("0.000000", CultureInfo.InvariantCulture) + "," + Lon.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        /// <summary>Great-circle distance in metres.</summary>
        public static double Distance(LatLng a, LatLng b)
        {
            const double R = 6371000;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180, dLon = (b.Lon - a.Lon) * Math.PI / 180;
            double la1 = a.Lat * Math.PI / 180, la2 = b.Lat * Math.PI / 180;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(la1) * Math.Cos(la2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        }

        /// <summary>Point at distance (m) and bearing (deg) from origin.</summary>
        public static LatLng Offset(LatLng o, double meters, double bearingDeg)
        {
            const double R = 6371000;
            double d = meters / R, br = bearingDeg * Math.PI / 180;
            double la1 = o.Lat * Math.PI / 180, lo1 = o.Lon * Math.PI / 180;
            double la2 = Math.Asin(Math.Sin(la1) * Math.Cos(d) + Math.Cos(la1) * Math.Sin(d) * Math.Cos(br));
            double lo2 = lo1 + Math.Atan2(Math.Sin(br) * Math.Sin(d) * Math.Cos(la1), Math.Cos(d) - Math.Sin(la1) * Math.Sin(la2));
            return new LatLng(la2 * 180 / Math.PI, lo2 * 180 / Math.PI);
        }
    }

    /// <summary>A route end point: the user's position, a Google place or a raw coordinate.</summary>
    public sealed class Waypoint
    {
        public bool IsMyLocation;
        public string PlaceId;
        public LatLng Location;
        public string Label;

        /// <summary>The user's default address (stands in for GPS).</summary>
        public static Waypoint MyLocation()
        {
            return new Waypoint
            {
                IsMyLocation = true,
                Label = AppSettings.MyLabel,
                Location = AppSettings.HasMyLocation ? new LatLng(AppSettings.MyLat, AppSettings.MyLon) : new LatLng()
            };
        }

        public static Waypoint FromPlace(Place p)
        {
            return new Waypoint { PlaceId = p.Id, Location = p.Location, Label = p.Name };
        }
    }
}
