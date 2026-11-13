using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    /// <summary>Directions: origin / destination, travel modes, alternatives on the map, route list, step list, options.</summary>
    public sealed partial class MainPage
    {
        private Waypoint _dirOrigin, _dirDest;
        private string _dirMode = "DRIVE";
        private readonly Dictionary<string, List<RouteInfo>> _routeCache = new Dictionary<string, List<RouteInfo>>();
        private readonly Dictionary<string, string> _routeErrors = new Dictionary<string, string>();
        private int _selectedRoute;
        private Screen _dirReturn = Screen.Home;
        private CancellationTokenSource _dirCts;

        private static readonly string[][] Modes =
        {
            new[] { "TWO_WHEELER", "Xe máy" },
            new[] { "DRIVE", "Ô tô" },
            new[] { "TRANSIT", "Công cộng" },
            new[] { "WALK", "Đi bộ" },
            new[] { "BICYCLE", "Xe đạp" }
        };

        private void DirectionsFab_Click(object sender, RoutedEventArgs e)
        {
            StartDirections(null);
        }

        private void StartDirections(Waypoint dest)
        {
            _dirReturn = _screen;
            _dirOrigin = AppSettings.HasMyLocation ? Waypoint.MyLocation() : null;   // empty origin when no default address
            _dirDest = dest;
            _dirMode = "TWO_WHEELER";   // directions always open on the motorbike tab
            _routeCache.Clear();
            _routeErrors.Clear();
            _routeRows.Clear();

            ClearResultPins();
            ClearPlacePins();
            SetScreen(Screen.Directions);
            UpdateDirFields();
            BuildModeTabs();
            _peekHeight = 170;
            SetSheet(SheetState.Peek);

            var ignored = ComputeRoutesAsync();   // with no destination: shows "Chọn điểm đến…"
            if (dest == null) OpenSearch(SearchPurpose.Destination, "");
        }

        private void UpdateDirFields()
        {
            DirOriginText.Text = _dirOrigin != null ? _dirOrigin.Label : "Chọn điểm xuất phát";
            DirDestText.Text = _dirDest != null ? _dirDest.Label : "Chọn điểm đến";
            DirOriginText.Foreground = ThemeBrush(_dirOrigin != null && _dirOrigin.IsMyLocation ? "PrimaryBrush" : "OnSurfaceBrush");
            DirDestText.Foreground = ThemeBrush(_dirDest == null ? "OnSurfaceVariantBrush" : (_dirDest.IsMyLocation ? "PrimaryBrush" : "OnSurfaceBrush"));
        }

        private void BuildModeTabs()
        {
            ModeTabsPanel.Children.Clear();
            ModeTabsPanel.ColumnDefinitions.Clear();
            for (int i = 0; i < Modes.Length; i++)
            {
                ModeTabsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                string mode = Modes[i][0];
                bool sel = mode == _dirMode;
                List<RouteInfo> cached;
                string dur = _routeCache.TryGetValue(mode, out cached) && cached.Count > 0 ? ShortDuration(cached[0].DurationSeconds) : "";

                var icon = new Path
                {
                    Data = PathDataConverter.Parse(ManeuverIcons.ForMode(mode)),
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Uniform,
                    Fill = ThemeBrush(sel ? "OnPrimaryContainerBrush" : "OnSurfaceVariantBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var text = new TextBlock
                {
                    Text = dur.Length > 0 ? dur : Modes[i][1],
                    FontSize = 11,
                    FontFamily = (FontFamily)Application.Current.Resources["AppFontMedium"],
                    Foreground = ThemeBrush(sel ? "OnPrimaryContainerBrush" : "OnSurfaceVariantBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(icon);
                stack.Children.Add(text);
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Background = sel ? ThemeBrush("PrimaryContainerBrush") : null,
                    Margin = new Thickness(2, 0, 2, 0),
                    Child = stack
                };
                var b = new Button { Style = (Style)Resources["BareButton"], Content = pill, Tag = mode };
                b.Click += ModeTab_Click;
                Grid.SetColumn(b, i);
                ModeTabsPanel.Children.Add(b);
            }
        }

        /// <summary>"25 ph", "1 g 20" – fits under the mode icons.</summary>
        private static string ShortDuration(int seconds)
        {
            int min = Math.Max(1, (int)Math.Round(seconds / 60.0));
            if (min < 60) return min + " phút";
            int h = min / 60, m = min % 60;
            if (h >= 24) return (h / 24) + " ngày";
            return m == 0 ? h + " giờ" : h + " g " + m;
        }

        private void ModeTab_Click(object sender, RoutedEventArgs e)
        {
            string mode = (sender as FrameworkElement).Tag as string;
            if (mode == null || mode == _dirMode) return;
            _dirMode = mode;
            AppSettings.TravelMode = mode;
            _selectedRoute = 0;
            BuildModeTabs();
            if (_routeCache.ContainsKey(mode)) ShowRoutes(true);
            else { var ignored = ComputeRoutesAsync(); }
        }

        /// <summary>A directions request is parked until the first GPS fix (see OnPosition).</summary>
        private bool _routeWaitsForPosition;

        private void SetDirStatus(string text, bool loading)
        {
            DirStatusPanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            DirStatusText.Text = text ?? "";
            DirLoading.IsActive = loading;
            DirLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            DirSummaryInfo.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task ComputeRoutesAsync()
        {
            if (_dirCts != null) _dirCts.Cancel();
            _dirCts = new CancellationTokenSource();
            var ct = _dirCts.Token;
            ClearRoutes();
            _routeRows.Clear();

            if (_dirDest == null || _dirOrigin == null)
            {
                SetDirStatus(_dirDest == null ? "Chọn điểm đến để xem đường đi." : "Chọn điểm xuất phát.", false);
                return;
            }

            string mode = _dirMode;
            SetDirStatus("Đang tìm đường…", true);
            try
            {
                LatLng originPos = _dirOrigin.Location, destPos = _dirDest.Location;
                if (_dirOrigin.IsMyLocation || _dirDest.IsMyLocation)
                {
                    if (!_myPos.HasValue)
                    {
                        SetDirStatus("Chưa đặt địa chỉ mặc định. Chạm vào ô điểm xuất phát để chọn, hoặc đặt địa chỉ mặc định trong Cài đặt.", false);
                        return;
                    }
                    if (_dirOrigin.IsMyLocation) originPos = _myPos.Value;
                    if (_dirDest.IsMyLocation) destPos = _myPos.Value;
                }

                if (originPos.IsZero) originPos = await ResolveWaypointAsync(_dirOrigin, ct);
                if (destPos.IsZero) destPos = await ResolveWaypointAsync(_dirDest, ct);
                if (originPos.IsZero || destPos.IsZero)
                {
                    SetDirStatus("Không tìm được vị trí của " + (originPos.IsZero ? _dirOrigin.Label : _dirDest.Label) + ".", false);
                    return;
                }
                if (RoutesService.UsesOsm(mode)) SetDirStatus("Đang tìm đường xe máy (OpenStreetMap)…", true);
                var routes = await RoutesService.ComputeAsync(originPos, destPos, mode, ct);
                DiagLog.Write("routes " + mode + " -> " + routes.Count + (routes.Count > 0 ? " pts=" + routes[0].Points.Count + " steps=" + routes[0].Steps.Count : ""));
                if (ct.IsCancellationRequested) return;
                _routeCache[mode] = routes;
                _routeErrors.Remove(mode);
                if (mode != _dirMode) return;
                _selectedRoute = 0;
                BuildModeTabs();
                ShowRoutes(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                SetDirStatus(MapsApi.Describe(ex), false);
            }
        }

        /// <summary>Coordinates for a waypoint that only has a label (old history entries): best search match.</summary>
        private async Task<LatLng> ResolveWaypointAsync(Waypoint w, CancellationToken ct)
        {
            if (w == null || string.IsNullOrEmpty(w.Label)) return new LatLng();
            string q = w.PlaceId != null && !w.PlaceId.StartsWith("ChIJ") ? w.PlaceId : w.Label;
            var r = await PlacesService.SearchAsync(q, BiasPoint, 30000, 0, ct);
            if (r.Places.Count == 0) return new LatLng();
            w.Location = r.Places[0].Location;
            return w.Location;
        }

        private void ShowRoutes(bool fit)
        {
            List<RouteInfo> routes;
            if (!_routeCache.TryGetValue(_dirMode, out routes) || routes.Count == 0)
            {
                ClearRoutes();
                _routeRows.Clear();
                string what = _dirMode == "TRANSIT" ? "Không có tuyến phương tiện công cộng phù hợp."
                            : _dirMode == "TWO_WHEELER" ? "Không tìm thấy tuyến xe máy giữa hai điểm này."
                            : _dirMode == "BICYCLE" ? "Không tìm thấy tuyến xe đạp giữa hai điểm này."
                            : "Không tìm thấy tuyến đường cho phương tiện này.";
                SetDirStatus(what, false);
                return;
            }
            if (_selectedRoute >= routes.Count) _selectedRoute = 0;
            var sel = routes[_selectedRoute];
            SetDirStatus(null, false);

            DirDurationText.Text = sel.DurationText;
            DirDurationText.Foreground = sel.DurationBrush;
            DirDistanceText.Text = sel.IsTransit ? "" : "(" + sel.DistanceText + ")";
            string sub = sel.Subtitle;
            if (!string.IsNullOrEmpty(sel.ViaText)) sub = sub.Length > 0 ? sub + "\n" + sel.ViaText : sel.ViaText;
            DirSubtitleText.Text = sub;
            DirSubtitleText.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            DirWarningText.Text = string.Join("\n", sel.Warnings);
            DirWarningText.Visibility = sel.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            _routeRows.Clear();
            foreach (var r in routes)
                _routeRows.Add(new RouteRow { Route = r, Selected = r.Index == _selectedRoute, Chips = r.IsTransit ? BuildRouteChips(r) : null });

            DrawRoutes(routes, _selectedRoute);
            _peekHeight = 22 + (DirSummary.ActualHeight > 0 ? DirSummary.ActualHeight : 150);
            if (_sheetState == SheetState.Peek) SetSheet(SheetState.Peek);
            if (fit)
            {
                var pts = new List<LatLng>();
                foreach (var r in routes) pts.AddRange(r.Points);
                var ignored = FitPointsAsync(pts);
            }
            UpdatePeekAfterLayout();
        }

        private async void UpdatePeekAfterLayout()
        {
            await Task.Delay(50);
            if (_screen != Screen.Directions) return;
            _peekHeight = 22 + (DirSummary.ActualHeight > 0 ? DirSummary.ActualHeight : 150);
            if (_sheetState == SheetState.Peek) SetSheet(SheetState.Peek);
        }

        private List<RouteChip> BuildRouteChips(RouteInfo r)
        {
            var chips = new List<RouteChip>();
            string lastMode = null;
            foreach (var s in r.Steps)
            {
                if (s.TravelMode == "TRANSIT" && s.Transit != null)
                {
                    chips.Add(new RouteChip
                    {
                        IconData = ManeuverIcons.ForVehicle(s.Transit.VehicleType),
                        ChipText = s.Transit.ChipText,
                        ChipBg = new SolidColorBrush(s.Transit.LineColor),
                        ChipFg = new SolidColorBrush(s.Transit.LineTextColor),
                        First = chips.Count == 0
                    });
                    lastMode = "TRANSIT";
                }
                else if (lastMode != "WALK")
                {
                    chips.Add(new RouteChip { IconData = Icons.Walk, First = chips.Count == 0 });
                    lastMode = "WALK";
                }
                if (chips.Count >= 7) break;
            }
            return chips;
        }

        private void SelectRoute(int index)
        {
            if (index == _selectedRoute) return;
            _selectedRoute = index;
            ShowRoutes(false);
        }

        private void DirRoutesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var row = e.ClickedItem as RouteRow;
            if (row == null) return;
            if (row.Route.Index == _selectedRoute) { OpenSteps(); return; }
            SelectRoute(row.Route.Index);
            SetSheet(SheetState.Peek);
        }

        // ------------------------------------------------------------ header buttons

        private void DirBack_Click(object sender, RoutedEventArgs e)
        {
            ExitDirections();
        }

        private void DirOrigin_Click(object sender, RoutedEventArgs e)
        {
            OpenSearch(SearchPurpose.Origin, _dirOrigin != null && !_dirOrigin.IsMyLocation ? _dirOrigin.Label : "");
        }

        private void DirDest_Click(object sender, RoutedEventArgs e)
        {
            OpenSearch(SearchPurpose.Destination, _dirDest != null && !_dirDest.IsMyLocation ? _dirDest.Label : "");
        }

        private void DirSwap_Click(object sender, RoutedEventArgs e)
        {
            var t = _dirOrigin;
            _dirOrigin = _dirDest;
            _dirDest = t;
            _routeCache.Clear();
            _selectedRoute = 0;
            UpdateDirFields();
            BuildModeTabs();
            var ignored = ComputeRoutesAsync();
        }

        /// <summary>Result of the search screen while picking a waypoint.</summary>
        private void PickWaypoint(Waypoint w)
        {
            var purpose = _searchPurpose;
            CloseSearch();
            if (_screen != Screen.Directions) return;
            if (purpose == SearchPurpose.Origin) _dirOrigin = w; else _dirDest = w;
            _routeCache.Clear();
            _selectedRoute = 0;
            UpdateDirFields();
            BuildModeTabs();
            var ignored = ComputeRoutesAsync();
        }

        private void ExitDirections()
        {
            if (_dirCts != null) _dirCts.Cancel();
            _routeWaitsForPosition = false;
            ClearRoutes();
            _routeRows.Clear();
            StepsScreen.Visibility = Visibility.Collapsed;

            if (_dirReturn == Screen.Place && _place != null)
            {
                SetScreen(Screen.Place);
                if (_placeFromResults) DrawResultPins(_place); else ShowPlacePin(_place);
                UpdatePlacePeek();
                SetSheet(SheetState.Peek);
                if (!_place.Location.IsZero) { var ignored = CenterOnAsync(_place.Location, Math.Max(MapView.ZoomLevel, 15)); }
            }
            else if (_dirReturn == Screen.Results && _results.Count > 0)
            {
                _searchBarTitle = _lastQuery ?? "";
                _peekHeight = 150;
                SetScreen(Screen.Results);
                DrawResultPins(null);
                SetSheet(SheetState.Half);
            }
            else
            {
                SetScreen(Screen.Home);
                SetSheet(SheetState.Hidden);
            }
        }

        private void DirShare_Click(object sender, RoutedEventArgs e)
        {
            List<RouteInfo> routes;
            if (_dirDest == null || !_routeCache.TryGetValue(_dirMode, out routes) || routes.Count == 0) return;
            var r = routes[Math.Min(_selectedRoute, routes.Count - 1)];
            string mode = _dirMode == "TRANSIT" ? "transit" : _dirMode == "WALK" ? "walking" : _dirMode == "BICYCLE" ? "bicycling" : _dirMode == "TWO_WHEELER" ? "two-wheeler" : "driving";
            string url = "https://www.google.com/maps/dir/?api=1&travelmode=" + mode +
                         "&origin=" + Uri.EscapeDataString(WaypointQuery(_dirOrigin, r, true)) +
                         "&destination=" + Uri.EscapeDataString(WaypointQuery(_dirDest, r, false));
            string text = "Đường đi từ " + _dirOrigin.Label + " đến " + _dirDest.Label + ": " + r.DurationText +
                          (r.IsTransit ? "" : " (" + r.DistanceText + ")") + "\n" + url;
            Share("Đường đi", text);
        }

        private string WaypointQuery(Waypoint w, RouteInfo r, bool start)
        {
            if (w.IsMyLocation)
            {
                if (_myPos.HasValue) return _myPos.Value.ToString();
                if (r.Points.Count > 0) return (start ? r.Points[0] : r.Points[r.Points.Count - 1]).ToString();
            }
            if (!w.Location.IsZero) return w.Location.ToString();
            return w.Label ?? "";
        }

        // ------------------------------------------------------------ route options

        private bool _optHighways, _optTolls, _optFerries;

        private void DirMore_Click(object sender, RoutedEventArgs e)
        {
            _optHighways = AppSettings.AvoidHighways;
            _optTolls = AppSettings.AvoidTolls;
            _optFerries = AppSettings.AvoidFerries;
            AvoidHighwaysCheck.IsChecked = _optHighways;
            AvoidTollsCheck.IsChecked = _optTolls;
            AvoidFerriesCheck.IsChecked = _optFerries;
            RouteOptionsOverlay.Visibility = Visibility.Visible;
        }

        private void RouteOptionsDone_Click(object sender, RoutedEventArgs e)
        {
            CloseRouteOptions(true);
        }

        private void RouteOptionsScrim_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            CloseRouteOptions(true);
        }

        private void CloseRouteOptions(bool apply)
        {
            RouteOptionsOverlay.Visibility = Visibility.Collapsed;
            if (!apply) return;
            bool h = AvoidHighwaysCheck.IsChecked == true, t = AvoidTollsCheck.IsChecked == true, f = AvoidFerriesCheck.IsChecked == true;
            if (h == _optHighways && t == _optTolls && f == _optFerries) return;
            AppSettings.AvoidHighways = h;
            AppSettings.AvoidTolls = t;
            AppSettings.AvoidFerries = f;
            _routeCache.Remove("DRIVE");
            _routeCache.Remove("TWO_WHEELER");
            BuildModeTabs();
            if (_screen == Screen.Directions && (_dirMode == "DRIVE" || _dirMode == "TWO_WHEELER"))
            {
                _selectedRoute = 0;
                var ignored = ComputeRoutesAsync();
            }
        }

        // ------------------------------------------------------------ steps

        private void DirSteps_Click(object sender, RoutedEventArgs e)
        {
            OpenSteps();
        }

        private void OpenSteps()
        {
            List<RouteInfo> routes;
            if (!_routeCache.TryGetValue(_dirMode, out routes) || routes.Count == 0) return;
            var r = routes[Math.Min(_selectedRoute, routes.Count - 1)];

            StepsDuration.Text = r.DurationText;
            StepsDuration.Foreground = r.DurationBrush;
            StepsDistance.Text = r.IsTransit ? "" : "(" + r.DistanceText + ")";
            StepsVia.Text = r.IsTransit ? r.Subtitle : r.ViaText;
            StepsVia.Visibility = string.IsNullOrEmpty(StepsVia.Text) ? Visibility.Collapsed : Visibility.Visible;

            var items = new List<StepItem>();
            var neutral = ThemeBrush("OnSurfaceVariantBrush");
            var primary = ThemeBrush("PrimaryBrush");
            items.Add(new StepItem
            {
                IconData = _dirOrigin.IsMyLocation ? Icons.MyLocation : Icons.PlaceFilled,
                IconBrush = primary,
                Title = _dirOrigin.Label,
                Points = r.Points.Count > 0 ? new List<LatLng> { r.Points[0] } : null
            });

            if (r.IsTransit) AddTransitSteps(r, items, neutral);
            else
            {
                foreach (var s in r.Steps)
                {
                    items.Add(new StepItem
                    {
                        IconData = ManeuverIcons.For(s.Maneuver, s.TravelMode ?? r.TravelMode),
                        IconBrush = neutral,
                        Title = string.IsNullOrEmpty(s.Instruction) ? "Tiếp tục" : s.Instruction,
                        Meta = s.DistanceText,
                        Points = s.Points
                    });
                }
            }

            items.Add(new StepItem
            {
                IconData = Icons.PlaceFilled,
                IconBrush = ThemeBrush("PinRedBrush"),
                Title = _dirDest.Label,
                Points = r.Points.Count > 0 ? new List<LatLng> { r.Points[r.Points.Count - 1] } : null
            });

            StepsList.ItemsSource = items;
            StepsScreen.Visibility = Visibility.Visible;
        }

        private void AddTransitSteps(RouteInfo r, List<StepItem> items, Brush neutral)
        {
            var steps = r.Steps;
            int i = 0;
            while (i < steps.Count)
            {
                var s = steps[i];
                if (s.TravelMode == "TRANSIT" && s.Transit != null)
                {
                    var t = s.Transit;
                    string meta = (t.StopCount > 0 ? t.StopCount + " điểm dừng · " : "") + s.DurationText;
                    if (!string.IsNullOrEmpty(t.ArrivalStop))
                        meta += "\nXuống tại " + t.ArrivalStop + (string.IsNullOrEmpty(t.ArrivalTime) ? "" : " (" + t.ArrivalTime + ")");
                    items.Add(new StepItem
                    {
                        IconData = ManeuverIcons.ForVehicle(t.VehicleType),
                        IconBrush = new SolidColorBrush(t.LineColor),
                        ChipText = (t.VehicleName != null ? t.VehicleName + " " : "") + t.ChipText,
                        ChipBg = new SolidColorBrush(t.LineColor),
                        ChipFg = new SolidColorBrush(t.LineTextColor),
                        Title = (string.IsNullOrEmpty(t.DepartureTime) ? "" : t.DepartureTime + " · ") + (t.DepartureStop ?? "Lên xe"),
                        Subtitle = string.IsNullOrEmpty(t.Headsign) ? null : "Hướng " + t.Headsign,
                        Meta = meta,
                        Points = s.Points
                    });
                    i++;
                    continue;
                }

                // merge consecutive walking steps
                int dist = 0, dur = 0;
                var pts = new List<LatLng>();
                while (i < steps.Count && !(steps[i].TravelMode == "TRANSIT" && steps[i].Transit != null))
                {
                    dist += steps[i].DistanceMeters;
                    dur += steps[i].DurationSeconds;
                    pts.AddRange(steps[i].Points);
                    i++;
                }
                string target = i < steps.Count && steps[i].Transit != null && !string.IsNullOrEmpty(steps[i].Transit.DepartureStop)
                    ? steps[i].Transit.DepartureStop : _dirDest.Label;
                items.Add(new StepItem
                {
                    IconData = Icons.Walk,
                    IconBrush = neutral,
                    Title = "Đi bộ đến " + target,
                    Meta = Fmt.Duration(dur) + " (" + Fmt.Distance(dist) + ")",
                    Points = pts
                });
            }
        }

        private void StepsBack_Click(object sender, RoutedEventArgs e)
        {
            StepsScreen.Visibility = Visibility.Collapsed;
        }

        private void StepsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as StepItem;
            if (item == null || item.Points == null || item.Points.Count == 0) return;
            StepsScreen.Visibility = Visibility.Collapsed;
            SetSheet(SheetState.Peek);
            if (item.Points.Count == 1) { var ig = CenterOnAsync(item.Points[0], 17); }
            else { var ignored = FitPointsAsync(item.Points); }
        }
    }
}
