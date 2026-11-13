using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.SpeechRecognition;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace GGMaps
{
    public enum SearchPurpose { Main, Origin, Destination, SetDefault }

    /// <summary>Full-screen search: recent searches, Places autocomplete, voice input, waypoint picking.</summary>
    public sealed partial class MainPage
    {
        private SearchPurpose _searchPurpose = SearchPurpose.Main;
        private string _sessionToken;
        private DispatcherTimer _acTimer;
        private CancellationTokenSource _acCts;
        private bool _suppressTextChanged;

        private void InitSearchTimer()
        {
            _acTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            _acTimer.Tick += AcTimer_Tick;
        }

        private async void OpenSearch(SearchPurpose purpose, string text)
        {
            _searchPurpose = purpose;
            _sessionToken = PlacesService.NewSessionToken();
            SearchBox.PlaceholderText = purpose == SearchPurpose.Origin ? "Chọn điểm xuất phát"
                                      : purpose == SearchPurpose.Destination ? "Chọn điểm đến"
                                      : purpose == SearchPurpose.SetDefault ? "Tìm địa chỉ mặc định" : "Tìm kiếm tại đây";
            _suppressTextChanged = true;
            SearchBox.Text = text ?? "";
            _suppressTextChanged = false;
            UpdateSearchButtons();
            SearchScreen.Visibility = Visibility.Visible;
            await LoadSuggestionsAsync(SearchBox.Text);
            await Task.Delay(60);
            try
            {
                SearchBox.Focus(FocusState.Programmatic);
                SearchBox.Select(SearchBox.Text.Length, 0);
            }
            catch { }
        }

        private void CloseSearch()
        {
            _acTimer.Stop();
            if (_acCts != null) _acCts.Cancel();
            SearchScreen.Visibility = Visibility.Collapsed;
            SearchProgress.Visibility = Visibility.Collapsed;
            try { this.Focus(FocusState.Programmatic); } catch { }   // hides the keyboard
        }

        private void SearchBack_Click(object sender, RoutedEventArgs e)
        {
            CloseSearch();
        }

        private void SearchClear_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            try { SearchBox.Focus(FocusState.Programmatic); } catch { }
        }

        private void UpdateSearchButtons()
        {
            bool has = !string.IsNullOrEmpty(SearchBox.Text);
            SearchClearButton.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            SearchMicButton.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchButtons();
            if (_suppressTextChanged) return;
            _acTimer.Stop();
            _acTimer.Start();
        }

        private async void AcTimer_Tick(object sender, object e)
        {
            _acTimer.Stop();
            await LoadSuggestionsAsync(SearchBox.Text);
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            string text = (SearchBox.Text ?? "").Trim();
            if (text.Length == 0) return;
            SubmitQuery(text);
        }

        private async Task LoadSuggestionsAsync(string text)
        {
            if (_acCts != null) _acCts.Cancel();
            _acCts = new CancellationTokenSource();
            var ct = _acCts.Token;
            text = (text ?? "").Trim();
            SearchEmptyText.Visibility = Visibility.Collapsed;

            var rows = new List<Suggestion>();
            if ((_searchPurpose == SearchPurpose.Origin || _searchPurpose == SearchPurpose.Destination) && AppSettings.HasMyLocation)
                rows.Add(new Suggestion { Kind = SuggestionKind.MyLocation, MainText = AppSettings.MyLabel, SecondaryText = "Địa chỉ mặc định" });

            if (text.Length == 0)
            {
                SearchHeaderText.Visibility = Visibility.Visible;
                SearchProgress.Visibility = Visibility.Collapsed;
                var history = await HistoryStore.LoadAsync();
                if (ct.IsCancellationRequested) return;
                foreach (var h in history)
                {
                    rows.Add(new Suggestion
                    {
                        Kind = SuggestionKind.History,
                        MainText = h.Text,
                        SecondaryText = h.Secondary,
                        PlaceId = h.PlaceId,
                        FullText = h.Text,
                        Location = new LatLng(h.Lat, h.Lon)
                    });
                }
                SetSuggestions(rows);
                if (history.Count == 0)
                {
                    SearchEmptyText.Text = "Các tìm kiếm gần đây sẽ hiện ở đây.";
                    SearchEmptyText.Visibility = Visibility.Visible;
                }
                return;
            }

            SearchHeaderText.Visibility = Visibility.Collapsed;
            SearchProgress.Visibility = Visibility.Visible;
            try
            {
                double span;
                var center = SearchCenter(out span);
                var list = await PlacesService.AutocompleteAsync(text, center, Math.Min(span, 15000), _myPos, ct);
                if (ct.IsCancellationRequested) return;
                DiagLog.Write("autocomplete -> " + list.Count);
                rows.AddRange(list);
                SetSuggestions(rows);
                if (list.Count == 0)
                {
                    SearchEmptyText.Text = "Không có gợi ý. Nhấn Enter để tìm “" + text + "”.";
                    SearchEmptyText.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                SetSuggestions(rows);
                SearchEmptyText.Text = MapsApi.Describe(ex);
                SearchEmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                if (!ct.IsCancellationRequested) SearchProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void SetSuggestions(List<Suggestion> rows)
        {
            _suggestions.Clear();
            foreach (var r in rows) _suggestions.Add(r);
        }

        private void SuggestionFill_Click(object sender, RoutedEventArgs e)
        {
            var s = (sender as FrameworkElement).Tag as Suggestion;
            if (s == null) return;
            SearchBox.Text = s.FullText ?? s.MainText ?? "";
            try
            {
                SearchBox.Focus(FocusState.Programmatic);
                SearchBox.Select(SearchBox.Text.Length, 0);
            }
            catch { }
        }

        private void SuggestionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var s = e.ClickedItem as Suggestion;
            if (s == null) return;

            if (s.Kind == SuggestionKind.MyLocation)
            {
                PickWaypoint(Waypoint.MyLocation());
                return;
            }
            if (s.Kind == SuggestionKind.Query || (s.Kind == SuggestionKind.History && string.IsNullOrEmpty(s.PlaceId)))
            {
                SubmitQuery(s.FullText ?? s.MainText);
                return;
            }

            // a place (prediction or remembered)
            var item = new HistoryItem { Text = s.MainText, Secondary = s.SecondaryText, PlaceId = s.PlaceId, Lat = s.Location.Lat, Lon = s.Location.Lon };
            var ignored = HistoryStore.AddAsync(item);
            if (_searchPurpose == SearchPurpose.SetDefault)
            {
                if (s.Location.IsZero) { SubmitQuery(s.PlaceId ?? s.FullText ?? s.MainText); return; }
                CloseSearch();
                SetDefaultAddress(s.Location, s.MainText + (string.IsNullOrEmpty(s.SecondaryText) ? "" : ", " + s.SecondaryText));
                return;
            }
            if (_searchPurpose == SearchPurpose.Main)
            {
                CloseSearch();
                // PlaceId holds the full suggestion text ("name, street, district…"), used to look the place up
                var p = new Place { Name = s.MainText, Address = s.PlaceId ?? s.SecondaryText, ShortAddress = s.SecondaryText, Location = s.Location };
                OpenPlace(p, false, _sessionToken);
            }
            else
            {
                PickWaypoint(new Waypoint { PlaceId = s.PlaceId, Label = s.MainText, Location = s.Location });
            }
        }

        /// <summary>Enter / query prediction: a text search (main) or the best match as a waypoint (picking).</summary>
        private async void SubmitQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var ignored = HistoryStore.AddAsync(new HistoryItem { Text = text });
            if (_searchPurpose == SearchPurpose.Main)
            {
                CloseSearch();
                await RunSearchAsync(text, false);
                return;
            }

            SearchProgress.Visibility = Visibility.Visible;
            try
            {
                var r = await PlacesService.SearchAsync(text, BiasPoint, 30000, 0, CancellationToken.None);
                var list = r.Places;
                if (list.Count == 0)
                {
                    SearchEmptyText.Text = "Không tìm thấy “" + text + "”.";
                    SearchEmptyText.Visibility = Visibility.Visible;
                    return;
                }
                if (_searchPurpose == SearchPurpose.SetDefault)
                {
                    CloseSearch();
                    SetDefaultAddress(list[0].Location, list[0].Name + (string.IsNullOrEmpty(list[0].ShortAddress) ? "" : ", " + list[0].ShortAddress));
                    return;
                }
                PickWaypoint(Waypoint.FromPlace(list[0]));
            }
            catch (Exception ex)
            {
                SearchEmptyText.Text = MapsApi.Describe(ex);
                SearchEmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                SearchProgress.Visibility = Visibility.Collapsed;
            }
        }

        // ------------------------------------------------------------ voice search

        private async void Mic_Click(object sender, RoutedEventArgs e)
        {
            string text = null;
            try
            {
                using (var rec = new SpeechRecognizer())
                {
                    rec.UIOptions.AudiblePrompt = "Nói tên địa điểm";
                    rec.UIOptions.ExampleText = "Ví dụ: cà phê gần đây";
                    await rec.CompileConstraintsAsync();
                    var result = await rec.RecognizeWithUIAsync();
                    if (result.Status == SpeechRecognitionResultStatus.Success) text = result.Text;
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("speech " + ex.Message);
                ShowToast("Không dùng được nhận dạng giọng nói (cần ngôn ngữ giọng nói trong Cài đặt của máy và quyền micro).", 4000);
                return;
            }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (SearchScreen.Visibility != Visibility.Visible) _searchPurpose = SearchPurpose.Main;
            SubmitQuery(text.Trim().TrimEnd('.'));
        }
    }
}
