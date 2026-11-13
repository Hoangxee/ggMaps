using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    /// <summary>Screen state machine (Home / Results / Place / Directions), top chrome, Back key, category chips.</summary>
    public sealed partial class MainPage
    {
        private bool _immersive;

        private void SetScreen(Screen s)
        {
            _screen = s;
            UpdateChrome();
        }

        private void UpdateChrome()
        {
            bool home = _screen == Screen.Home;
            bool dir = _screen == Screen.Directions;

            TopChrome.Visibility = dir || (home && _immersive) ? Visibility.Collapsed : Visibility.Visible;
            DirectionsHeader.Visibility = dir ? Visibility.Visible : Visibility.Collapsed;
            ChipsScroller.Visibility = home ? Visibility.Visible : Visibility.Collapsed;

            SearchBarPin.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            SearchBarBackButton.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            SearchBarMicButton.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            SearchBarClearButton.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
            if (home)
            {
                SearchBarText.Text = "Tìm kiếm tại đây";
                SearchBarText.Foreground = ThemeBrush("OnSurfaceVariantBrush");
            }
            else
            {
                SearchBarText.Text = _searchBarTitle ?? "";
                SearchBarText.Foreground = ThemeBrush("OnSurfaceBrush");
            }

            DirectionsFab.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
            AttributionPanel.Margin = new Thickness(10, 0, home ? 120 : 76, 0);
            MyLocationButton.Visibility = home && _immersive ? Visibility.Collapsed : Visibility.Visible;

            ResultsPanel.Visibility = _screen == Screen.Results ? Visibility.Visible : Visibility.Collapsed;
            PlacePanel.Visibility = _screen == Screen.Place ? Visibility.Visible : Visibility.Collapsed;
            DirectionsPanel.Visibility = dir ? Visibility.Visible : Visibility.Collapsed;
            if (_screen != Screen.Results) SearchAreaButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>Back key: innermost overlay first, then screen by screen; false = leave the app.</summary>
        private bool HandleBack()
        {
            if (PhotoViewer.Visibility == Visibility.Visible) { ClosePhotoViewer(); return true; }
            if (SettingsScreen.Visibility == Visibility.Visible) { CloseSettings(); return true; }
            if (RouteOptionsOverlay.Visibility == Visibility.Visible) { CloseRouteOptions(false); return true; }
            if (LayersOverlay.Visibility == Visibility.Visible) { LayersOverlay.Visibility = Visibility.Collapsed; return true; }
            if (StepsScreen.Visibility == Visibility.Visible) { StepsScreen.Visibility = Visibility.Collapsed; return true; }
            if (SearchScreen.Visibility == Visibility.Visible) { CloseSearch(); return true; }

            if (_screen != Screen.Home && _sheetState == SheetState.Full)
            {
                SetSheet(SheetState.Half);
                return true;
            }

            switch (_screen)
            {
                case Screen.Directions: ExitDirections(); return true;
                case Screen.Place: ClosePlace(); return true;
                case Screen.Results: ClearResults(); return true;
            }
            if (_immersive) { SetImmersive(false); return true; }
            return false;
        }

        private void SetImmersive(bool on)
        {
            _immersive = on;
            UpdateChrome();
        }

        // ------------------------------------------------------------ search bar (top chrome)

        private void SearchBar_Click(object sender, RoutedEventArgs e)
        {
            OpenSearch(SearchPurpose.Main, _screen == Screen.Home ? "" : (_lastQuery ?? _searchBarTitle));
        }

        private void SearchBarBack_Click(object sender, RoutedEventArgs e)
        {
            HandleBack();
        }

        /// <summary>X in the search bar: back to the plain map.</summary>
        private void SearchBarClear_Click(object sender, RoutedEventArgs e)
        {
            CancelPlaceLoad();
            _place = null;
            ClearPlacePins();
            ClearResults();
        }

        private void Avatar_Click(object sender, RoutedEventArgs e)
        {
            OpenSettings(false);
        }

        // ------------------------------------------------------------ category chips

        private static readonly string[][] Categories =
        {
            new[] { "Nhà hàng", Icons.Restaurant },
            new[] { "Cà phê", Icons.Cafe },
            new[] { "Cây xăng", Icons.Gas },
            new[] { "Khách sạn", Icons.Hotel },
            new[] { "Mua sắm", Icons.Shopping },
            new[] { "ATM", Icons.Atm },
            new[] { "Bệnh viện", Icons.Hospital },
            new[] { "Nhà thuốc", Icons.Pharmacy },
            new[] { "Bãi đỗ xe", Icons.Parking }
        };

        private void BuildChips()
        {
            ChipsPanel.Children.Clear();
            foreach (var c in Categories)
            {
                var icon = new Path
                {
                    Data = PathDataConverter.Parse(c[1]),
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    Fill = ThemeBrush("OnSurfaceVariantBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var text = new TextBlock
                {
                    Text = c[0],
                    FontSize = 14,
                    FontFamily = (FontFamily)Application.Current.Resources["AppFontMedium"],
                    Foreground = ThemeBrush("OnSurfaceBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 1)
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(icon);
                row.Children.Add(text);
                var border = new Border
                {
                    Height = 34,
                    CornerRadius = new CornerRadius(17),
                    Padding = new Thickness(12, 0, 14, 0),
                    Background = ThemeBrush("ChipBrush"),
                    BorderBrush = ThemeBrush("FloatingEdgeBrush"),
                    BorderThickness = new Thickness(1),
                    Child = row
                };
                var button = new Button
                {
                    Style = (Style)Resources["BareButton"],
                    Content = border,
                    Tag = c[0],
                    Margin = new Thickness(0, 0, 8, 0)
                };
                button.Click += CategoryChip_Click;
                ChipsPanel.Children.Add(button);
            }
        }

        private void CategoryChip_Click(object sender, RoutedEventArgs e)
        {
            var q = (sender as FrameworkElement).Tag as string;
            if (string.IsNullOrEmpty(q)) return;
            var ignored = RunSearchAsync(q, false);
        }
    }
}
