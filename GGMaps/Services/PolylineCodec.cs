using System.Collections.Generic;

namespace GGMaps
{
    /// <summary>Google "Encoded Polyline Algorithm Format" decoder (precision 1e5; Valhalla uses 1e6).</summary>
    public static class PolylineCodec
    {
        public static List<LatLng> Decode(string encoded, double precision = 1e5)
        {
            var points = new List<LatLng>();
            if (string.IsNullOrEmpty(encoded)) return points;
            int index = 0, lat = 0, lng = 0, len = encoded.Length;
            while (index < len)
            {
                int result = 0, shift = 0, b;
                do
                {
                    if (index >= len) return points;
                    b = encoded[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);
                lat += ((result & 1) != 0) ? ~(result >> 1) : (result >> 1);

                result = 0; shift = 0;
                do
                {
                    if (index >= len) return points;
                    b = encoded[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);
                lng += ((result & 1) != 0) ? ~(result >> 1) : (result >> 1);

                points.Add(new LatLng(lat / precision, lng / precision));
            }
            return points;
        }
    }
}
