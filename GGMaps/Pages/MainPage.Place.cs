using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Calls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace GGMaps
{
    /// <summary>Place sheet (summary, actions, photos, overview / reviews / about tabs), dropped pin, photo viewer.</summary>
    public sealed partial class MainPage
    {
        private bool _placeFromResults;
        private CancellationTokenSource _placeCts;
        private int _placeTab;
        private bool _hoursExpanded;

        private void CancelPlaceLoad()
        {
            if (_placeCts != null) _placeCts.Cancel();
            _placeCts = null;
        }

        private async void OpenPlace(Place p, bool fromResults, string sessionToken)
        {
            if (p == null) return;
            CancelPlaceLoad();
            _placeCts = new CancellationTokenSource();
            var ct = _placeCts.Token;

            _place = p;
            _placeFromResults = fromResults;
            if (!fromResults) _searchBarTitle = p.Name ?? "";
            _hoursExpanded = false;
            SetScreen(Screen.Place);
            SetPlaceTab(0);
            RenderPlace(p, true);
            PlaceScroll.ChangeView(null, 0, null, true);

            if (fromResults) DrawResultPins(p);
            else { ClearResultPins(); ShowPlacePin(p); }

            await Task.Delay(30);   // let the header measure itself
            UpdatePlacePeek();
            SetSheet(SheetState.Peek);
            if (!p.Location.IsZero) await CenterOnAsync(p.Location, Math.Max(MapView.ZoomLevel, 16));

            if (p.IsDroppedPin || string.IsNullOrEmpty(p.Name)) return;
            if (p.HasDetails) { RenderPlace(p, false); return; }

            PlaceLoading.Visibility = Visibility.Visible;
            PlaceLoading.IsActive = true;
            PlaceErrorText.Visibility = Visibility.Collapsed;
            try
            {
                var d = await PlacesService.GetDetailsAsync(p, ct);
                if (ct.IsCancellationRequested || _place != p) return;
                DiagLog.Write("details " + (d == null ? "none" : "reviews=" + d.Reviews.Count + " photos=" + d.Photos.Count + " hours=" + d.WeekdayHours.Count));
                if (d == null) { d = p; d.HasDetails = true; }
                bool hadLocation = !p.Location.IsZero;
                d.DistanceMeters = _myPos.HasValue && !d.Location.IsZero ? LatLng.Distance(_myPos.Value, d.Location) : p.DistanceMeters;
                d.Index = p.Index;
                _place = d;
                if (!fromResults) _searchBarTitle = d.Name ?? "";
                UpdateChrome();
                RenderPlace(d, false);
                if (!fromResults) ShowPlacePin(d);
                await Task.Delay(30);
                UpdatePlacePeek();
                if (_sheetState == SheetState.Peek) SetSheet(SheetState.Peek);
                if (!hadLocation && !d.Location.IsZero) await CenterOnAsync(d.Location, Math.Max(MapView.ZoomLevel, 16));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                PlaceErrorText.Text = MapsApi.Describe(ex);
                PlaceErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    PlaceLoading.IsActive = false;
                    PlaceLoading.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>Peek = handle + header (name, rating, type, hours) + action pills.</summary>
        private void UpdatePlacePeek()
        {
            double header = PlaceHeader.ActualHeight > 0 ? PlaceHeader.ActualHeight : 90;
            _peekHeight = 22 + header + 14 + 40 + 16;
        }

        private void RenderPlace(Place p, bool summaryOnly)
        {
            bool pin = p.IsDroppedPin;
            PlaceTitle.Text = p.Name ?? "";

            PlaceRatingPanel.Visibility = p.HasRating ? Visibility.Visible : Visibility.Collapsed;
            PlaceRatingText.Text = p.RatingText;
            PlaceStarsText.Text = p.StarsText;
            PlaceRatingCountText.Text = p.RatingCountText;

            string meta = pin ? (p.Address ?? "Đang tìm địa chỉ…") : p.MetaLine;
            PlaceMetaText.Text = meta;
            PlaceMetaText.Visibility = string.IsNullOrEmpty(meta) ? Visibility.Collapsed : Visibility.Visible;

            PlaceOpenPanel.Visibility = p.OpenVisibility;
            PlaceOpenStatus.Text = p.OpenStatus;
            PlaceOpenStatus.Foreground = p.OpenBrush;
            PlaceOpenDetail.Text = p.OpenDetail;

            PlaceCallButton.Visibility = string.IsNullOrEmpty(p.Phone) && string.IsNullOrEmpty(p.IntlPhone) ? Visibility.Collapsed : Visibility.Visible;
            PlaceWebsiteButton.Visibility = string.IsNullOrEmpty(p.Website) ? Visibility.Collapsed : Visibility.Visible;

            // photos
            PlacePhotosPanel.Children.Clear();
            for (int i = 0; i < p.Photos.Count && i < 10; i++)
            {
                var ph = p.Photos[i];
                if (string.IsNullOrEmpty(ph.Url)) continue;
                bool first = i == 0;
                double w = first ? 200 : 130, h = 150;
                var img = new Image { Stretch = Stretch.UniformToFill };
                var bmp = new BitmapImage { DecodePixelHeight = 300 };
                bmp.UriSource = new Uri(ph.Url);
                img.Source = bmp;
                var border = new Border
                {
                    Width = w,
                    Height = h,
                    CornerRadius = new CornerRadius(10),
                    Background = ThemeBrush("SurfaceHighBrush"),
                    Child = img
                };
                var b = new Button { Style = (Style)Resources["BareButton"], Content = border, Tag = i, Margin = new Thickness(0, 0, 6, 0) };
                b.Click += PlacePhoto_Click;
                PlacePhotosPanel.Children.Add(b);
            }
            PlacePhotosScroller.Visibility = PlacePhotosPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // tabs only when there is something behind them
            bool details = !pin && !summaryOnly;
            PlaceTabs.Visibility = details ? Visibility.Visible : Visibility.Collapsed;

            // overview rows
            AddressText.Text = p.Address ?? "";
            AddressRow.Visibility = string.IsNullOrEmpty(p.Address) ? Visibility.Collapsed : Visibility.Visible;

            HoursRow.Visibility = p.WeekdayHours.Count > 0 || p.OpenNow.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (p.OpenStatus.Length > 0)
            {
                HoursStatusText.Text = p.OpenStatus;
                HoursStatusText.Foreground = p.OpenBrush;
                HoursDetailText.Text = p.OpenDetail;
            }
            else
            {
                HoursStatusText.Text = "Giờ mở cửa";
                HoursStatusText.Foreground = ThemeBrush("OnSurfaceBrush");
                HoursDetailText.Text = "";
            }
            HoursList.Children.Clear();
            int today = 0;   // Google lists today first
            for (int i = 0; i < p.WeekdayHours.Count; i++)
            {
                HoursList.Children.Add(new TextBlock
                {
                    Text = p.WeekdayHours[i],
                    FontSize = 14,
                    FontFamily = (FontFamily)Application.Current.Resources[i == today ? "AppFontMedium" : "ContentFont"],
                    Foreground = ThemeBrush(i == today ? "OnSurfaceBrush" : "OnSurfaceVariantBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }
            HoursChevron.Visibility = p.WeekdayHours.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyHoursExpanded();

            WebsiteText.Text = ShortUrl(p.Website);
            WebsiteRow.Visibility = string.IsNullOrEmpty(p.Website) ? Visibility.Collapsed : Visibility.Visible;
            PhoneText.Text = p.Phone ?? p.IntlPhone ?? "";
            PhoneRow.Visibility = string.IsNullOrEmpty(PhoneText.Text) ? Visibility.Collapsed : Visibility.Visible;

            string code = p.PlusCode;
            if (pin || string.IsNullOrEmpty(code))
            {
                string coord = p.Location.Lat.ToString("0.00000", CultureInfo.InvariantCulture) + ", " + p.Location.Lon.ToString("0.00000", CultureInfo.InvariantCulture);
                code = string.IsNullOrEmpty(code) ? coord : code + "\n" + coord;
            }
            PlusCodeText.Text = code;
            PlusCodeRow.Visibility = p.Location.IsZero ? Visibility.Collapsed : Visibility.Visible;

            // reviews
            ReviewsBigRating.Text = p.HasRating ? p.RatingText : "–";
            ReviewsStars.Text = p.HasRating ? p.StarsText : "";
            ReviewsCount.Text = p.HasRating ? p.RatingCount.ToString("#,0", Fmt.Vi) + " bài đánh giá" : "";
            ReviewsList.ItemsSource = p.Reviews;
            NoReviewsText.Visibility = p.Reviews.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // about
            AboutText.Text = !string.IsNullOrEmpty(p.Summary) ? p.Summary
                           : (string.IsNullOrEmpty(p.TypeLabel) ? "Chưa có thông tin giới thiệu." : p.TypeLabel + (string.IsNullOrEmpty(p.Address) ? "" : "\n" + p.Address));

            if (!details) SetPlaceTab(0);
        }

        private static string ShortUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string s = url;
            int i = s.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0) s = s.Substring(i + 3);
            if (s.StartsWith("www.")) s = s.Substring(4);
            return s.TrimEnd('/');
        }

        // ------------------------------------------------------------ tabs / hours

        private void PlaceTab_Click(object sender, RoutedEventArgs e)
        {
            int tab;
            if (int.TryParse((sender as FrameworkElement).Tag as string, out tab)) SetPlaceTab(tab);
        }

        private void SetPlaceTab(int tab)
        {
            _placeTab = tab;
            OverviewPanel.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
            ReviewsPanel.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
            AboutPanel.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
            var on = ThemeBrush("PrimaryBrush");
            var off = ThemeBrush("OnSurfaceVariantBrush");
            TabOverviewText.Foreground = tab == 0 ? on : off;
            TabReviewsText.Foreground = tab == 1 ? on : off;
            TabAboutText.Foreground = tab == 2 ? on : off;
            TabOverviewLine.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
            TabReviewsLine.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
            TabAboutLine.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HoursRow_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null || _place.WeekdayHours.Count == 0) return;
            _hoursExpanded = !_hoursExpanded;
            ApplyHoursExpanded();
        }

        private void ApplyHoursExpanded()
        {
            HoursList.Visibility = _hoursExpanded && HoursList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            HoursChevron.Data = PathDataConverter.Parse(_hoursExpanded ? "M12 8l-6 6 1.41 1.41L12 10.83l4.59 4.58L18 14z" : "M16.59 8.59L12 13.17 7.41 8.59 6 10l6 6 6-6z");
        }

        // ------------------------------------------------------------ actions

        private void PlaceDirections_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null) return;
            StartDirections(_place.IsDroppedPin
                ? new Waypoint { Location = _place.Location, Label = _place.Address ?? _place.Name }
                : Waypoint.FromPlace(_place));
        }

        private void PlaceCall_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null) return;
            string number = _place.Phone ?? _place.IntlPhone;
            if (string.IsNullOrEmpty(number)) return;
            try { PhoneCallManager.ShowPhoneCallUI(number, _place.Name ?? ""); }
            catch (Exception ex) { ShowToast("Không gọi được: " + ex.Message); }
        }

        private void PlaceWebsite_Click(object sender, RoutedEventArgs e)
        {
            if (_place != null) LaunchUri(_place.Website);
        }

        private void PlaceShare_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null) return;
            string url = _place.MapsUri;
            string text = (_place.Name ?? "") + (string.IsNullOrEmpty(_place.Address) ? "" : "\n" + _place.Address) + "\n" + url;
            Share(_place.Name, text);
        }

        private void PlaceSetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null || _place.Location.IsZero) return;
            string label = _place.IsDroppedPin
                ? (!string.IsNullOrEmpty(_place.Address) ? _place.Address : _place.Location.ToString())
                : _place.Name + (string.IsNullOrEmpty(_place.ShortAddress) ? "" : ", " + _place.ShortAddress);
            SetDefaultAddress(_place.Location, label);
        }

        private void AddressRow_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null || _place.Location.IsZero) return;
            SetSheet(SheetState.Peek);
            var ignored = CenterOnAsync(_place.Location, Math.Max(MapView.ZoomLevel, 17));
        }

        private void PlaceClose_Click(object sender, RoutedEventArgs e)
        {
            ClosePlace();
        }

        private void ClosePlace()
        {
            CancelPlaceLoad();
            ClearPlacePins();
            _place = null;
            if (_placeFromResults && _results.Count > 0)
            {
                _searchBarTitle = _lastQuery ?? "";
                _peekHeight = 150;
                SetScreen(Screen.Results);
                DrawResultPins(null);
                SetSheet(SheetState.Half);
            }
            else
            {
                ClearResults();
            }
        }

        // ------------------------------------------------------------ dropped pin

        private async void DropPin(LatLng at)
        {
            var p = new Place { IsDroppedPin = true, Name = "Ghim đã thả", Location = at };
            _searchBarTitle = "Ghim đã thả";
            OpenPlace(p, false, null);
            try
            {
                var g = await PlacesService.ReverseGeocodeAsync(at, CancellationToken.None);
                if (_place != p) return;
                p.Address = g.Address;
                p.PlusCode = g.PlusCode;
                RenderPlace(p, true);
                await Task.Delay(30);
                UpdatePlacePeek();
                if (_sheetState == SheetState.Peek) SetSheet(SheetState.Peek);
            }
            catch (Exception ex)
            {
                if (_place != p) return;
                p.Address = null;
                PlaceMetaText.Text = MapsApi.Describe(ex);
                PlaceMetaText.Visibility = Visibility.Visible;
            }
        }

        // ------------------------------------------------------------ photo viewer

        private void PlacePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (_place == null) return;
            int index = (int)(sender as FrameworkElement).Tag;
            foreach (var ph in _place.Photos)
                if (ph.LargeUrl == null) ph.LargeUrl = PlacesService.PhotoUrl(ph.Name, 960, 960);
            PhotoFlip.ItemsSource = _place.Photos;
            PhotoFlip.SelectedIndex = Math.Min(index, _place.Photos.Count - 1);
            PhotoViewer.Visibility = Visibility.Visible;
            ApplyViewerStatusBar(true);
            UpdatePhotoAuthor();
        }

        private void PhotoFlip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePhotoAuthor();
        }

        private void UpdatePhotoAuthor()
        {
            var ph = PhotoFlip.SelectedItem as PlacePhoto;
            int n = _place != null ? _place.Photos.Count : 0;
            string author = ph != null && !string.IsNullOrEmpty(ph.Author) ? "Ảnh: " + ph.Author : "";
            PhotoAuthorText.Text = (PhotoFlip.SelectedIndex + 1) + " / " + n + (author.Length > 0 ? "   ·   " + author : "");
        }

        private void PhotoClose_Click(object sender, RoutedEventArgs e)
        {
            ClosePhotoViewer();
        }

        private void ClosePhotoViewer()
        {
            PhotoViewer.Visibility = Visibility.Collapsed;
            PhotoFlip.ItemsSource = null;
            ApplyViewerStatusBar(false);
        }
    }
}
