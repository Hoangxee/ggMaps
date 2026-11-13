using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace GGMaps
{
    /// <summary>Text search results: list in the sheet, red pins, client-side filters, "search this area".</summary>
    public sealed partial class MainPage
    {
        private CancellationTokenSource _searchCts;
        private bool _filterOpen, _filterRating, _sortDistance;
        private LatLng _resultsCenter;
        private double _resultsZoom;
        private bool _resultsCameraKnown;

        private async Task RunSearchAsync(string query, bool inArea)
        {
            if (_searchCts != null) _searchCts.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            SetBusy(true);
            try
            {
                double radius;
                var center = SearchCenter(out radius, inArea);
                var r = await PlacesService.SearchAsync(query, center, radius, 0, ct);
                var list = r.Places;
                DiagLog.Write("search \"" + query + "\" -> " + list.Count + (r.Single ? " (single)" : ""));
                if (ct.IsCancellationRequested) return;

                foreach (var p in list)
                    p.DistanceMeters = _myPos.HasValue && !p.Location.IsZero ? LatLng.Distance(_myPos.Value, p.Location) : double.NaN;

                if (list.Count == 0)
                {
                    ShowToast("Không tìm thấy kết quả cho “" + query + "”.");
                    return;
                }

                _lastQuery = query;
                _searchBarTitle = query;
                CancelPlaceLoad();
                ClearPlacePins();

                if ((r.Single || list.Count == 1) && !inArea)
                {
                    _results.Clear();
                    _allResults = list;
                    ClearResultPins();
                    OpenPlace(list[0], false, null);
                    return;
                }

                if (!inArea)
                {
                    _filterOpen = _filterRating = false;
                    _sortDistance = _myPos.HasValue;   // nearest to the default address first
                }
                _allResults = list;
                _peekHeight = 150;
                SetScreen(Screen.Results);
                ApplyResultFilters();
                SetSheet(SheetState.Half);
                SearchAreaButton.Visibility = Visibility.Collapsed;
                if (!inArea) await FitPointsAsync(_results.Select(x => x.Location));
                RememberResultsCamera();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ShowToast(MapsApi.Describe(ex), 5000);
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>
        /// Where to search: around the default address whenever one is set (results are then sorted by distance from it);
        /// around the visible map for "Tìm kiếm khu vực này" or when there is no default address.
        /// span = metres across the search viewport.
        /// </summary>
        private LatLng SearchCenter(out double span, bool inArea = false)
        {
            var c = LatLng.From(MapView.Center);
            span = 6000;
            LatLng nw, se;
            if (TryGetViewport(out nw, out se)) span = Math.Max(800, Math.Min(100000, LatLng.Distance(nw, se)));
            if (!inArea && _myPos.HasValue)
            {
                c = _myPos.Value;
                span = 10000;
            }
            else if (inArea)
            {
                // keep the visible area
            }
            else if (_myPos.HasValue && LatLng.Distance(_myPos.Value, c) < Math.Max(span * 0.75, 4000))
            {
                c = _myPos.Value;
                span = Math.Min(span, 12000);
            }
            else if (!_myPos.HasValue && _approxPos.HasValue && span > 30000 && LatLng.Distance(_approxPos.Value, c) < span)
            {
                // zoomed far out without GPS: search the user's city rather than the whole country
                c = _approxPos.Value;
                span = 30000;
            }
            return c;
        }

        private async void RememberResultsCamera()
        {
            _resultsCameraKnown = false;
            await Task.Delay(1500);
            _resultsCenter = LatLng.From(MapView.Center);
            _resultsZoom = MapView.ZoomLevel;
            _resultsCameraKnown = true;
        }

        private void ApplyResultFilters()
        {
            IEnumerable<Place> q = _allResults;
            if (_filterOpen) q = q.Where(p => p.OpenNow == true);
            if (_filterRating) q = q.Where(p => p.Rating >= 4.0);
            if (_sortDistance) q = q.OrderBy(p => double.IsNaN(p.DistanceMeters) ? double.MaxValue : p.DistanceMeters);

            _results.Clear();
            int i = 0;
            foreach (var p in q) { p.Index = i++; _results.Add(p); }
            ResultsEmptyText.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DrawResultPins(null);
            UpdateFilterChip(FilterOpenBorder, FilterOpenText, _filterOpen);
            UpdateFilterChip(FilterRatingBorder, FilterRatingText, _filterRating);
            UpdateFilterChip(SortDistanceBorder, SortDistanceText, _sortDistance);
            SortDistanceButton.Visibility = _myPos.HasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateFilterChip(Border border, TextBlock text, bool on)
        {
            border.Background = on ? ThemeBrush("ChipSelectedBrush") : null;
            border.BorderBrush = on ? ThemeBrush("ChipSelectedBrush") : ThemeBrush("OutlineVariantBrush");
            text.Foreground = on ? ThemeBrush("OnPrimaryContainerBrush") : ThemeBrush("OnSurfaceVariantBrush");
        }

        private void FilterOpen_Click(object sender, RoutedEventArgs e) { _filterOpen = !_filterOpen; ApplyResultFilters(); }
        private void FilterRating_Click(object sender, RoutedEventArgs e) { _filterRating = !_filterRating; ApplyResultFilters(); }
        private void SortDistance_Click(object sender, RoutedEventArgs e) { _sortDistance = !_sortDistance; ApplyResultFilters(); }

        private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var p = e.ClickedItem as Place;
            if (p != null) OpenPlace(p, true, null);
        }

        private void ResultDirections_Click(object sender, RoutedEventArgs e)
        {
            var p = (sender as FrameworkElement).Tag as Place;
            if (p != null) StartDirections(Waypoint.FromPlace(p));
        }

        // ------------------------------------------------------------ "search this area"

        private void CheckSearchArea()
        {
            if (_screen != Screen.Results || !_resultsCameraKnown || string.IsNullOrEmpty(_lastQuery)) return;
            LatLng nw, se;
            if (!TryGetViewport(out nw, out se)) return;
            double viewSize = LatLng.Distance(nw, se);
            double moved = LatLng.Distance(_resultsCenter, LatLng.From(MapView.Center));
            bool show = moved > viewSize * 0.25 || Math.Abs(MapView.ZoomLevel - _resultsZoom) >= 1;
            SearchAreaButton.Margin = new Thickness(0, TopChrome.ActualHeight + 6, 0, 0);
            SearchAreaButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchArea_Click(object sender, RoutedEventArgs e)
        {
            SearchAreaButton.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(_lastQuery)) { var ignored = RunSearchAsync(_lastQuery, true); }
        }

        private void ClearResults()
        {
            if (_searchCts != null) _searchCts.Cancel();
            ClearResultPins();
            _results.Clear();
            _allResults = new List<Place>();
            _lastQuery = null;
            _searchBarTitle = "";
            _resultsCameraKnown = false;
            SetScreen(Screen.Home);
            SetSheet(SheetState.Hidden);
        }
    }
}
