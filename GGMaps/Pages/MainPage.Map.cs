using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    /// <summary>Map control: Google tiles, camera helpers, markers, route polylines, attribution.</summary>
    public sealed partial class MainPage
    {
        private bool _tilesApplied;
        private DispatcherTimer _idleTimer;
        private DateTime _programmaticUntil = DateTime.MinValue;
        private DateTime _pinTappedAt = DateTime.MinValue;

        private readonly List<FrameworkElement> _resultPins = new List<FrameworkElement>();
        private FrameworkElement _placePin;
        private readonly List<MapElement> _routeElements = new List<MapElement>();
        private readonly List<FrameworkElement> _routeMarkers = new List<FrameworkElement>();

        private void InitMap()
        {
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _idleTimer.Tick += IdleTimer_Tick;
            // reload the tiles as soon as a connection appears (failed tiles are retried on the next redraw)
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
            MapView.MapTapped += MapView_MapTapped;
            MapView.MapHolding += MapView_MapHolding;
            MapView.CenterChanged += MapView_CenterChanged;
            MapView.ZoomLevelChanged += MapView_ZoomLevelChanged;
            MapView.HeadingChanged += MapView_HeadingChanged;
            try
            {
                MarkProgrammaticMove(3000);   // the initial camera is not a user pan (it would cancel "centre on first fix")
                MapView.Center = new LatLng(AppSettings.LastLat, AppSettings.LastLon).ToGeopoint();
                MapView.ZoomLevel = AppSettings.LastZoom;
                ApplyMapBackground();
            }
            catch (Exception ex) { DiagLog.Write("InitMap " + DiagLog.Describe(ex)); }

        }

        private void ApplyMapBackground()
        {
            string type = AppSettings.MapType;
            if (type == "satellite") MapView.Background = new SolidColorBrush(Color.FromArgb(255, 0x1B, 0x1F, 0x24));
            else if (Fmt.IsDark && type == "roadmap") MapView.Background = new SolidColorBrush(Color.FromArgb(255, 0x24, 0x2F, 0x3E));
            else MapView.Background = new SolidColorBrush(Color.FromArgb(255, 0xE8, 0xEA, 0xED));
        }

        private bool _wasOffline;

        private async void NetworkInformation_NetworkStatusChanged(object sender)
        {
            bool offline = MapsApi.IsOffline;
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                DiagLog.Write("network changed offline=" + offline);
                if (_wasOffline && !offline)
                {
                    ShowToast("Đã có kết nối Internet, đang tải lại bản đồ…");
                    var ignored = ApplyTilesAsync();
                }
                _wasOffline = offline;
            });
        }

        // ------------------------------------------------------------ tiles

        private Task ApplyTilesAsync()
        {
            try
            {
                string layers = TileService.Layers(AppSettings.MapType, AppSettings.Traffic);
                string scale = AppSettings.LabelScale == "scaleFactor2x" ? "2" : "1";
                bool dark = Fmt.IsDark;
                bool traffic = AppSettings.Traffic;
                _wasOffline = MapsApi.IsOffline;
                // live traffic changes: a 5-minute bucket in the cache key forces fresh traffic tiles
                string key = layers + "|" + scale + "|" + dark + (traffic ? "|" + (DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(5).Ticks) : "");
                MapView.SetTileSource((x, y, z) =>
                {
                    string url = TileService.TileUrl(layers, dark, scale, x, y, z);
                    return traffic ? url + "&ts=" + key.GetHashCode() : url;
                }, key);
                _tilesApplied = true;
                ApplyMapBackground();
                AttributionText.Text = TileService.Attribution;
                DiagLog.Write("tiles: " + layers + " scale=" + scale + " dark=" + dark + (_wasOffline ? " OFFLINE" : ""));
                if (_wasOffline) ShowToast(MapsApi.OfflineMessage, 6000);
            }
            catch (Exception ex)
            {
                DiagLog.Write("tiles " + DiagLog.Describe(ex));
                ShowToast("Không tải được bản đồ: " + MapsApi.Describe(ex), 6000);
            }
            return Task.FromResult(0);
        }

        // ------------------------------------------------------------ map events

        private void MapView_MapTapped(GMap sender, MapTapArgs args)
        {
            if ((DateTime.Now - _pinTappedAt).TotalMilliseconds < 400) return;
            if (_screen == Screen.Home) { SetImmersive(!_immersive); return; }
            if (_screen == Screen.Place && _place != null && _place.IsDroppedPin) { ClosePlace(); return; }
            if (_screen != Screen.Directions && _sheetState == SheetState.Full) SetSheet(SheetState.Half);
        }

        private void MapView_MapHolding(GMap sender, MapTapArgs args)
        {
            if (_screen == Screen.Directions) return;
            DropPin(LatLng.From(args.Location));
        }

        private void MapView_CenterChanged(GMap sender, object args)
        {
            if (DateTime.Now > _programmaticUntil) OnUserMovedMap();
            RestartIdleTimer();
        }

        private void MapView_ZoomLevelChanged(GMap sender, object args)
        {
            RestartIdleTimer();
        }

        private void MapView_HeadingChanged(GMap sender, object args)
        {
            double h = MapView.Heading;
            bool rotated = h > 0.5 && h < 359.5;
            CompassButton.Visibility = rotated ? Visibility.Visible : Visibility.Collapsed;
            CompassRotate.Angle = -h;
            UpdateHeadingCone();
        }

        private void Compass_Click(object sender, RoutedEventArgs e)
        {
            if (_locMode == LocMode.Compass) SetLocMode(LocMode.Follow);
            MarkProgrammaticMove();
            var ignored = MapView.TrySetViewAsync(MapView.Center, null, 0, null, MapAnimationKind.Default);
        }

        private void RestartIdleTimer()
        {
            _idleTimer.Stop();
            _idleTimer.Start();
        }

        private void IdleTimer_Tick(object sender, object e)
        {
            _idleTimer.Stop();
            var ignored = UpdateAttributionAsync();
            CheckSearchArea();
        }

        private void MarkProgrammaticMove(int ms = 1800)
        {
            _programmaticUntil = DateTime.Now.AddMilliseconds(ms);
        }

        private void SaveCamera()
        {
            try
            {
                var c = MapView.Center.Position;
                AppSettings.LastLat = c.Latitude;
                AppSettings.LastLon = c.Longitude;
                AppSettings.LastZoom = MapView.ZoomLevel;
            }
            catch { }
        }

        // ------------------------------------------------------------ attribution (required by the Map Tiles API terms)

        private Task UpdateAttributionAsync()
        {
            AttributionText.Text = TileService.Attribution;
            return Task.FromResult(0);
        }

        /// <summary>North-west / south-east corners of the visible map.</summary>
        private bool TryGetViewport(out LatLng nw, out LatLng se)
        {
            nw = se = new LatLng();
            try
            {
                double w = MapView.ActualWidth, h = MapView.ActualHeight;
                if (w <= 0 || h <= 0) return false;
                Geopoint a, b;
                MapView.GetLocationFromOffset(new Point(0, 0), out a);
                MapView.GetLocationFromOffset(new Point(w, h), out b);
                if (a == null || b == null) return false;
                nw = new LatLng(Math.Max(a.Position.Latitude, b.Position.Latitude), Math.Min(a.Position.Longitude, b.Position.Longitude));
                se = new LatLng(Math.Min(a.Position.Latitude, b.Position.Latitude), Math.Max(a.Position.Longitude, b.Position.Longitude));
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ camera helpers

        /// <summary>Top of the unobstructed map area (below the search bar / directions header).</summary>
        private double ChromeBottom
        {
            get
            {
                if (_screen == Screen.Directions) return DirectionsHeader.ActualHeight;
                return TopChrome.Visibility == Visibility.Visible ? TopChrome.ActualHeight : 0;
            }
        }

        /// <summary>Visible sheet height once the current animation ends.</summary>
        private double SheetTargetHeight { get { return VisibleHeightFor(_sheetState); } }

        /// <summary>Centres p in the free area between the chrome and the sheet, at the given zoom.</summary>
        private async Task CenterOnAsync(LatLng p, double zoom)
        {
            try
            {
                double h = MapView.ActualHeight;
                double top = ChromeBottom, bottom = SheetTargetHeight;
                double freeCenterY = top + (h - top - bottom) / 2;
                double shiftPx = h / 2 - freeCenterY;   // > 0: place must appear above the map centre

                double latPerPx = 0;
                Geopoint g1, g2;
                MapView.GetLocationFromOffset(new Point(MapView.ActualWidth / 2, h / 2), out g1);
                MapView.GetLocationFromOffset(new Point(MapView.ActualWidth / 2, h / 2 + 100), out g2);
                if (g1 != null && g2 != null)
                    latPerPx = (g1.Position.Latitude - g2.Position.Latitude) / 100.0 * Math.Pow(2, MapView.ZoomLevel - zoom);
                // rough correction for the latitude difference between current view and target (Mercator)
                double k = Math.Cos(p.Lat * Math.PI / 180) / Math.Max(0.01, Math.Cos(g1 != null ? g1.Position.Latitude * Math.PI / 180 : p.Lat * Math.PI / 180));
                var center = new LatLng(p.Lat - latPerPx * k * shiftPx, p.Lon);

                MarkProgrammaticMove();
                await MapView.TrySetViewAsync(center.ToGeopoint(), zoom, null, null, MapAnimationKind.Default);
            }
            catch (Exception ex) { DiagLog.Write("center " + ex.Message); }
        }

        /// <summary>Fits all points between the chrome and the sheet.</summary>
        private async Task FitPointsAsync(IEnumerable<LatLng> points)
        {
            double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;
            int n = 0;
            LatLng only = new LatLng();
            foreach (var p in points)
            {
                if (p.IsZero) continue;
                n++;
                only = p;
                minLat = Math.Min(minLat, p.Lat); maxLat = Math.Max(maxLat, p.Lat);
                minLon = Math.Min(minLon, p.Lon); maxLon = Math.Max(maxLon, p.Lon);
            }
            if (n == 0) return;
            if (n == 1 || (maxLat - minLat < 0.0005 && maxLon - minLon < 0.0005))
            {
                await CenterOnAsync(only, Math.Max(MapView.ZoomLevel, 16));
                return;
            }
            try
            {
                var box = new GeoboundingBox(
                    new BasicGeoposition { Latitude = maxLat, Longitude = minLon },
                    new BasicGeoposition { Latitude = minLat, Longitude = maxLon });
                var margin = new Thickness(36, ChromeBottom + 30, 36, SheetTargetHeight + 30);
                MarkProgrammaticMove();
                await MapView.TrySetViewBoundsAsync(box, margin, MapAnimationKind.Default);
            }
            catch (Exception ex) { DiagLog.Write("fit " + ex.Message); }
        }

        // ------------------------------------------------------------ markers

        /// <summary>Red Google-style pin, optional label on its right. Anchor = bottom centre.</summary>
        private FrameworkElement MakePin(string label, bool big, object tag)
        {
            double w = big ? 34 : 24, h = big ? 44 : 31;
            var grid = new Grid { Width = w, Height = h, Tag = tag };
            grid.Children.Add(new Path
            {
                Data = PathDataConverter.Parse("M12 0C5.37 0 0 5.37 0 12c0 9 12 20 12 20s12-11 12-20C24 5.37 18.63 0 12 0z"),
                Stretch = Stretch.Fill,
                Fill = ThemeBrush("PinRedBrush"),
                Stroke = ThemeBrush("PinDarkRedBrush"),
                StrokeThickness = 1
            });
            grid.Children.Add(new Ellipse
            {
                Width = w * 0.36,
                Height = w * 0.36,
                Fill = ThemeBrush("PinDarkRedBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, w * 0.32, 0, 0)
            });
            if (!string.IsNullOrEmpty(label))
            {
                grid.Children.Add(new TextBlock
                {
                    Text = label.Length > 26 ? label.Substring(0, 25) + "…" : label,
                    FontSize = 12,
                    FontFamily = (FontFamily)Application.Current.Resources["AppFontMedium"],
                    Foreground = new SolidColorBrush(Fmt.IsDark ? Color.FromArgb(255, 0xF2, 0x8B, 0x82) : Color.FromArgb(255, 0xB3, 0x14, 0x12)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(w + 2, 2, -180, 0),
                    Width = 180,
                    TextWrapping = TextWrapping.NoWrap,
                    IsHitTestVisible = false
                });
            }
            return grid;
        }

        private void AddMapChild(FrameworkElement el, LatLng at, Point anchor)
        {
            GMap.SetLocation(el, at.ToGeopoint());
            GMap.SetNormalizedAnchorPoint(el, anchor);
            MapView.Markers.Add(el);
        }

        private void RemoveMapChild(FrameworkElement el)
        {
            if (el == null) return;
            try { MapView.Markers.Remove(el); } catch { }
        }

        private void DrawResultPins(Place selected)
        {
            ClearResultPins();
            foreach (var p in _results)
            {
                if (p.Location.IsZero) continue;
                bool big = p == selected;
                var pin = MakePin(big ? null : p.Name, big, p);
                pin.Tapped += ResultPin_Tapped;
                if (big) Canvas.SetZIndex(pin, 10);
                AddMapChild(pin, p.Location, new Point(0.5, 1));
                _resultPins.Add(pin);
            }
        }

        private void ClearResultPins()
        {
            foreach (var pin in _resultPins) RemoveMapChild(pin);
            _resultPins.Clear();
        }

        private void ResultPin_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            _pinTappedAt = DateTime.Now;
            e.Handled = true;
            var p = (sender as FrameworkElement).Tag as Place;
            if (p != null) OpenPlace(p, true, null);
        }

        private void ShowPlacePin(Place p)
        {
            ClearPlacePins();
            if (p == null || p.Location.IsZero) return;
            _placePin = MakePin(null, true, p);
            Canvas.SetZIndex(_placePin, 10);
            AddMapChild(_placePin, p.Location, new Point(0.5, 1));
        }

        private void ClearPlacePins()
        {
            RemoveMapChild(_placePin);
            _placePin = null;
        }

        // ------------------------------------------------------------ routes

        private static readonly Color TrafficSlow = Color.FromArgb(255, 0xF2, 0x9D, 0x38);
        private static readonly Color TrafficJam = Color.FromArgb(255, 0xE5, 0x39, 0x35);

        private void ClearRoutes()
        {
            foreach (var el in _routeElements) { try { MapView.MapElements.Remove(el); } catch { } }
            _routeElements.Clear();
            foreach (var m in _routeMarkers) RemoveMapChild(m);
            _routeMarkers.Clear();
        }

        private void AddPolyline(List<LatLng> pts, Color color, double thickness, int z, bool dashed)
        {
            if (pts == null || pts.Count < 2) return;
            var positions = new List<BasicGeoposition>(pts.Count);
            foreach (var p in pts) positions.Add(new BasicGeoposition { Latitude = p.Lat, Longitude = p.Lon });
            var line = new MapPolyline
            {
                Path = new Geopath(positions),
                StrokeColor = color,
                StrokeThickness = thickness,
                StrokeDashed = dashed,
                ZIndex = z
            };
            MapView.MapElements.Add(line);
            _routeElements.Add(line);
        }

        private void DrawRoutes(List<RouteInfo> routes, int selected)
        {
            ClearRoutes();
            if (routes == null || routes.Count == 0) return;

            // alternates underneath
            for (int i = 0; i < routes.Count; i++)
            {
                if (i == selected) continue;
                var r = routes[i];
                AddPolyline(r.Points, Fmt.IsDark ? Color.FromArgb(255, 0x3C, 0x40, 0x43) : Color.FromArgb(255, 0x8C, 0x9B, 0xB3), 10, 1, false);
                AddPolyline(r.Points, Fmt.RouteAltColor, 7, 2, false);
            }

            var sel = routes[selected];
            bool dashed = sel.TravelMode == "WALK";
            if (sel.IsTransit)
            {
                foreach (var s in sel.Steps)
                {
                    if (s.TravelMode == "TRANSIT" && s.Transit != null)
                    {
                        AddPolyline(s.Points, Fmt.RouteBorder, 11, 3, false);
                        AddPolyline(s.Points, s.Transit.LineColor, 8, 4, false);
                    }
                    else
                    {
                        AddPolyline(s.Points, Fmt.RouteBlue, 7, 4, true);
                    }
                }
            }
            else
            {
                if (!dashed) AddPolyline(sel.Points, Fmt.RouteBorder, 11, 3, false);
                AddPolyline(sel.Points, Fmt.RouteBlue, dashed ? 7 : 8, 4, dashed);
                foreach (var si in sel.Speed)
                {
                    if (si.Speed != "SLOW" && si.Speed != "TRAFFIC_JAM") continue;
                    int a = Math.Max(0, si.Start), b = Math.Min(sel.Points.Count - 1, si.End);
                    if (b - a < 1) continue;
                    AddPolyline(sel.Points.GetRange(a, b - a + 1), si.Speed == "SLOW" ? TrafficSlow : TrafficJam, 8, 5, false);
                }
            }

            // duration bubbles (tap = select)
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r.Points.Count < 2) continue;
                double frac = i == selected ? 0.5 : 0.3 + 0.2 * (i % 3);
                var at = r.Points[(int)(r.Points.Count * frac) % r.Points.Count];
                bool isSel = i == selected;
                var bubble = new Border
                {
                    Background = isSel ? new SolidColorBrush(Fmt.RouteBlue) : ThemeBrush("FloatingBrush"),
                    BorderBrush = ThemeBrush("FloatingEdgeBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3, 8, 4),
                    Tag = i,
                    Child = new TextBlock
                    {
                        Text = r.DurationText,
                        FontSize = 13,
                        FontFamily = (FontFamily)Application.Current.Resources["AppFontMedium"],
                        Foreground = isSel ? new SolidColorBrush(Colors.White) : ThemeBrush("OnSurfaceBrush")
                    }
                };
                bubble.Tapped += RouteBubble_Tapped;
                Canvas.SetZIndex(bubble, isSel ? 6 : 5);
                AddMapChild(bubble, at, new Point(0.5, 1.2));
                _routeMarkers.Add(bubble);
            }

            // origin (white dot) / destination (red pin)
            var start = sel.Points.Count > 0 ? sel.Points[0] : new LatLng();
            var end = sel.Points.Count > 0 ? sel.Points[sel.Points.Count - 1] : new LatLng();
            if (!start.IsZero && !(_dirOrigin != null && _dirOrigin.IsMyLocation && _myPos.HasValue))
            {
                var dot = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new SolidColorBrush(Colors.White),
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0x20, 0x21, 0x24)),
                    StrokeThickness = 3
                };
                AddMapChild(dot, start, new Point(0.5, 0.5));
                _routeMarkers.Add(dot);
            }
            if (!end.IsZero)
            {
                var pin = MakePin(null, true, null);
                AddMapChild(pin, end, new Point(0.5, 1));
                _routeMarkers.Add(pin);
            }
        }

        private void RouteBubble_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            _pinTappedAt = DateTime.Now;
            e.Handled = true;
            var tag = (sender as FrameworkElement).Tag;
            if (tag is int) SelectRoute((int)tag);
        }
    }
}
