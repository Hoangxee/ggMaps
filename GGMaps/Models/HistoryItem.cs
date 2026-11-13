using System;

namespace GGMaps
{
    /// <summary>A recent search kept on the phone (search screen "Gần đây").</summary>
    public sealed class HistoryItem
    {
        public string Text { get; set; }
        public string Secondary { get; set; }
        public string PlaceId { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public DateTime Time { get; set; }
    }
}
