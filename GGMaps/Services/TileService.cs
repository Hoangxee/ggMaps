using System;

namespace GGMaps
{
    /// <summary>
    /// Google map tiles from the public tile servers used by Google Maps web (mt0-3.google.com/vt), no key:
    /// lyrs=m roadmap, s satellite, y hybrid (satellite + labels), p terrain; ",traffic" adds live traffic;
    /// apistyle = dark palette in the compact "s.t/s.e/p.c" styling syntax.
    /// </summary>
    public static class TileService
    {
        private const string DarkStyle =
            "s.e:g|p.c:#ff242f3e," +
            "s.e:l.t.f|p.c:#ff8a8f98," +
            "s.e:l.t.s|p.c:#ff242f3e," +
            "s.t:2|s.e:l.t.f|p.c:#ffd59563," +       // poi labels
            "s.t:40|s.e:g|p.c:#ff263c3f," +          // parks
            "s.t:3|s.e:g|p.c:#ff38414e," +           // roads
            "s.t:3|s.e:g.s|p.c:#ff212a37," +
            "s.t:3|s.e:l.t.f|p.c:#ff9ca5b3," +
            "s.t:49|s.e:g|p.c:#ff746855," +          // highways
            "s.t:49|s.e:l.t.f|p.c:#fff3d19c," +
            "s.t:4|s.e:g|p.c:#ff2f3948," +           // transit
            "s.t:6|s.e:g|p.c:#ff17263c," +           // water
            "s.t:6|s.e:l.t.f|p.c:#ff515c6d";

        public static string Layers(string mapType, bool traffic)
        {
            string l = mapType == "satellite" ? "y" : mapType == "terrain" ? "p" : "m";
            return traffic ? l + ",traffic" : l;
        }

        /// <summary>Tile URL for one tile (server picked from x / y so requests spread over mt0..mt3).</summary>
        public static string TileUrl(string layers, bool dark, string scale, int x, int y, int z)
        {
            int server = (x + y) & 3;
            // plain http: HttpMapTileDataSource on Windows Phone 8.1 does not request https tiles reliably
            string url = "http://mt" + server + ".google.com/vt/lyrs=" + layers +
                         "&hl=" + AppSettings.Language + "&gl=" + AppSettings.Region +
                         "&x=" + x + "&y=" + y + "&z=" + z;
            if (scale == "2") url += "&scale=2";
            if (dark && layers.StartsWith("m")) url += "&apistyle=" + Uri.EscapeDataString(DarkStyle);
            return url;
        }

        /// <summary>Static template for HttpMapTileDataSource (UriRequested overrides it per tile).</summary>
        public static string Template(string layers, bool dark, string scale)
        {
            return TileUrl(layers, dark, scale, 0, 0, 0)
                .Replace("&x=0&y=0&z=0", "&x={x}&y={y}&z={zoomlevel}")
                .Replace("http://mt0.", "http://mt1.");
        }

        public static string Attribution { get { return "Dữ liệu bản đồ ©" + DateTime.Now.Year + " Google"; } }
    }
}
