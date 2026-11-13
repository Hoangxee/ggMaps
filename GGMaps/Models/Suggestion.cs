using Windows.UI.Xaml;

namespace GGMaps
{
    public enum SuggestionKind { Place, Query, History, MyLocation }

    /// <summary>One row of the search screen (autocomplete prediction, query prediction or recent search).</summary>
    public sealed class Suggestion
    {
        public SuggestionKind Kind { get; set; }
        public string PlaceId { get; set; }
        public string MainText { get; set; }
        public string SecondaryText { get; set; }
        public string DistanceText { get; set; }
        /// <summary>Full text put into the search box when the "fill" arrow is tapped / query run.</summary>
        public string FullText { get; set; }
        public LatLng Location { get; set; }

        public string IconData
        {
            get
            {
                switch (Kind)
                {
                    case SuggestionKind.History: return Icons.History;
                    case SuggestionKind.Query: return Icons.Search;
                    case SuggestionKind.MyLocation: return Icons.MyLocation;
                    default: return Icons.PlaceFilled;
                }
            }
        }

        public Visibility SecondaryVisibility { get { return string.IsNullOrEmpty(SecondaryText) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility DistanceVisibility { get { return string.IsNullOrEmpty(DistanceText) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility FillVisibility { get { return Kind == SuggestionKind.MyLocation ? Visibility.Collapsed : Visibility.Visible; } }
    }
}
