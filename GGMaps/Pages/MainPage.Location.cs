using System;
using System.Collections.Generic;
using Windows.Devices.Geolocation;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    public enum LocMode { None, Follow, Compass }

    /// <summary>GPS (blue dot + accuracy circle + heading cone), compass, "my location" button modes.</summary>
    public sealed partial class MainPage
    {
        private Geolocator _geo;
        private LatLng? _myPos;
        private double _myAccuracy;
        private bool _locationDenied;
        private bool _centerOnFirstFix = true;
        private LocMode _locMode = LocMode.None;

        private Compass _compass;
        private double _compassHeading = double.NaN;

        private Grid _myDot;
        private RotateTransform _coneRotate;
        private FrameworkElement _cone;
        private MapPolygon _accuracyCircle;

        /// <summary>GPS is not used: "my location" is the default address chosen by the user (none = no blue dot).</summary>
        private void StartLocation()
        {
            if (!AppSettings.HasMyLocation)
            {
                _myPos = null;
                RemoveMapChild(_myDot);
                _myDot = null;
                _cone = null;
                if (_accuracyCircle != null) { try { MapView.MapElements.Remove(_accuracyCircle); } catch { } _accuracyCircle = null; }
                return;
            }
            var pos = new LatLng(AppSettings.MyLat, AppSettings.MyLon);
            if (!_myPos.HasValue || LatLng.Distance(_myPos.Value, pos) > 1) OnPosition(pos, 0);
        }

        /// <summary>Saves the default address and moves the blue dot there.</summary>
        private void SetDefaultAddress(LatLng p, string label)
        {
            AppSettings.SetMyLocation(p.Lat, p.Lon, label);
            StartLocation();
            if (SettingsScreen.Visibility == Visibility.Visible) UpdateMyLocationText();
            ShowToast("Đã đặt địa chỉ mặc định: " + AppSettings.MyLabel, 3500);
        }

        private void StartGpsUnused()
        {
            if (_geo != null) return;
            try
            {
                _geo = new Geolocator { DesiredAccuracy = PositionAccuracy.High, MovementThreshold = 3 };
                _geo.PositionChanged += Geo_PositionChanged;
                _geo.StatusChanged += Geo_StatusChanged;
                DiagLog.Write("geo start status=" + _geo.LocationStatus);
                // ask once explicitly: shows the consent prompt and gives a first fix quickly (PositionChanged alone may stay silent)
                var ignored = RequestPositionAsync(TimeSpan.FromSeconds(20));
            }
            catch (Exception ex)
            {
                _geo = null;
                _locationDenied = true;
                DiagLog.Write("geo " + ex.Message);
            }
        }

        private void StopLocation()
        {
            if (_geo == null) return;
            try
            {
                _geo.PositionChanged -= Geo_PositionChanged;
                _geo.StatusChanged -= Geo_StatusChanged;
            }
            catch { }
            _geo = null;
        }

        private async void Geo_StatusChanged(Geolocator sender, StatusChangedEventArgs args)
        {
            var status = args.Status;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                DiagLog.Write("geo status " + status);
                if (status == PositionStatus.Disabled && _routeWaitsForPosition && _screen == Screen.Directions)
                {
                    _routeWaitsForPosition = false;
                    SetDirStatus("Vị trí đã bị tắt. Bật Vị trí trong Cài đặt của điện thoại hoặc chọn điểm xuất phát khác.", false);
                }
                _locationDenied = status == PositionStatus.Disabled || status == PositionStatus.NotAvailable;
            });
        }

        private async void Geo_PositionChanged(Geolocator sender, PositionChangedEventArgs args)
        {
            var c = args.Position.Coordinate;
            var p = new LatLng(c.Point.Position.Latitude, c.Point.Position.Longitude);
            double acc = c.Accuracy;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => OnPosition(p, acc));
        }

        private bool _requestingPosition;

        /// <summary>One-shot position request (a cached fix up to 2 minutes old is fine). Null when unavailable.</summary>
        private async System.Threading.Tasks.Task<LatLng?> RequestPositionAsync(TimeSpan timeout)
        {
            if (_requestingPosition)
            {
                // another request is running: wait for its result
                var until = DateTime.Now + timeout;
                while (_requestingPosition && DateTime.Now < until && !_myPos.HasValue) await System.Threading.Tasks.Task.Delay(300);
                return _myPos;
            }
            _requestingPosition = true;
            try
            {
                // Default accuracy may answer from Wi-Fi / cell towers within seconds; the High-accuracy _geo
                // (PositionChanged) keeps refining with GPS. A fix up to 10 minutes old is accepted.
                var geo = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
                var pos = await geo.GetGeopositionAsync(TimeSpan.FromMinutes(10), timeout);
                var c = pos.Coordinate;
                var p = new LatLng(c.Point.Position.Latitude, c.Point.Position.Longitude);
                DiagLog.Write("geo fix acc=" + Math.Round(c.Accuracy) + "m src=" + c.PositionSource);
                OnPosition(p, c.Accuracy);
                return p;
            }
            catch (UnauthorizedAccessException)
            {
                _locationDenied = true;
                DiagLog.Write("geo denied (Location is off or blocked for this app)");
            }
            catch (Exception ex)
            {
                DiagLog.Write("geo request failed " + ex.GetType().Name + " " + ex.Message);
            }
            finally
            {
                _requestingPosition = false;
            }
            return null;
        }

        private void OnPosition(LatLng p, double accuracy)
        {
            bool first = !_myPos.HasValue;
            _myPos = p;
            _myAccuracy = accuracy;
            _locationDenied = false;
            UpdateMyDot();
            if (first) DiagLog.Write("geo first position acc=" + Math.Round(accuracy) + "m");

            if (_routeWaitsForPosition)
            {
                _routeWaitsForPosition = false;
                if (_screen == Screen.Directions) { var ig = ComputeRoutesAsync(); }
            }

            if (_centerOnFirstFix)
            {
                _centerOnFirstFix = false;
                if (_screen == Screen.Home)
                {
                    MarkProgrammaticMove();
                    var ignored = MapView.TrySetViewAsync(p.ToGeopoint(), Math.Max(MapView.ZoomLevel, 15), null, null, MapAnimationKind.Default);
                }
            }
            else if (_locMode != LocMode.None)
            {
                MarkProgrammaticMove();
                var ignored = MapView.TrySetViewAsync(p.ToGeopoint(), null, null, null, MapAnimationKind.Linear);
            }
        }

        private void UpdateMyDot()
        {
            if (!_myPos.HasValue) return;
            var p = _myPos.Value;
            if (_myDot == null)
            {
                _myDot = new Grid { Width = 64, Height = 64, IsHitTestVisible = false };
                // heading cone (a soft blue wedge pointing up, rotated with the compass)
                var cone = new Path
                {
                    Data = PathDataConverter.Parse("M32 32 L18 4 A30 30 0 0 1 46 4 Z"),
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 1),
                        EndPoint = new Point(0.5, 0),
                        GradientStops =
                        {
                            new GradientStop { Color = Color.FromArgb(0x99, 0x1A, 0x73, 0xE8), Offset = 0 },
                            new GradientStop { Color = Color.FromArgb(0x00, 0x1A, 0x73, 0xE8), Offset = 1 }
                        }
                    },
                    Width = 64,
                    Height = 64,
                    Visibility = Visibility.Collapsed,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                _coneRotate = new RotateTransform();
                cone.RenderTransform = _coneRotate;
                _cone = cone;
                _myDot.Children.Add(cone);
                _myDot.Children.Add(new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Colors.White) });
                _myDot.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(255, 0x1A, 0x73, 0xE8)) });
                Canvas.SetZIndex(_myDot, 20);
                AddMapChild(_myDot, p, new Point(0.5, 0.5));
            }
            else
            {
                GMap.SetLocation(_myDot, p.ToGeopoint());
            }

            // accuracy circle
            try
            {
                if (_accuracyCircle != null) MapView.MapElements.Remove(_accuracyCircle);
                _accuracyCircle = null;
                if (_myAccuracy > 15 && _myAccuracy < 5000)
                {
                    var ring = new List<BasicGeoposition>();
                    for (int i = 0; i < 36; i++)
                    {
                        var q = LatLng.Offset(p, _myAccuracy, i * 10);
                        ring.Add(new BasicGeoposition { Latitude = q.Lat, Longitude = q.Lon });
                    }
                    _accuracyCircle = new MapPolygon
                    {
                        Path = new Geopath(ring),
                        FillColor = Color.FromArgb(0x26, 0x1A, 0x73, 0xE8),
                        StrokeColor = Color.FromArgb(0x55, 0x1A, 0x73, 0xE8),
                        StrokeThickness = 1,
                        ZIndex = 0
                    };
                    MapView.MapElements.Add(_accuracyCircle);
                }
            }
            catch { }
            UpdateHeadingCone();
        }

        // ------------------------------------------------------------ compass

        private void StartCompass()
        {
            if (_compass != null) return;
            try
            {
                _compass = Compass.GetDefault();
                if (_compass == null) return;
                _compass.ReportInterval = Math.Max(_compass.MinimumReportInterval, 100u);
                _compass.ReadingChanged += Compass_ReadingChanged;
            }
            catch (Exception ex)
            {
                _compass = null;
                DiagLog.Write("compass " + ex.Message);
            }
        }

        private void StopCompass()
        {
            if (_compass == null) return;
            try { _compass.ReadingChanged -= Compass_ReadingChanged; } catch { }
            _compass = null;
        }

        private async void Compass_ReadingChanged(Compass sender, CompassReadingChangedEventArgs args)
        {
            var r = args.Reading;
            double h = r.HeadingTrueNorth.HasValue ? r.HeadingTrueNorth.Value : r.HeadingMagneticNorth;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => OnCompass(h));
        }

        private void OnCompass(double heading)
        {
            if (!double.IsNaN(_compassHeading) && Math.Abs(AngleDiff(heading, _compassHeading)) < 2) return;
            _compassHeading = heading;
            if (_locMode == LocMode.Compass && Math.Abs(AngleDiff(MapView.Heading, heading)) > 3)
            {
                MarkProgrammaticMove(600);
                MapView.Heading = heading;
            }
            UpdateHeadingCone();
        }

        private static double AngleDiff(double a, double b)
        {
            double d = (a - b) % 360;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            return d;
        }

        private void UpdateHeadingCone()
        {
            if (_cone == null) return;
            if (double.IsNaN(_compassHeading))
            {
                _cone.Visibility = Visibility.Collapsed;
                return;
            }
            _cone.Visibility = Visibility.Visible;
            _coneRotate.Angle = _compassHeading - MapView.Heading;
        }

        // ------------------------------------------------------------ "my location" button

        private void OnUserMovedMap()
        {
            if (_locMode != LocMode.None) SetLocMode(LocMode.None);
            _centerOnFirstFix = false;
        }

        private void SetLocMode(LocMode mode)
        {
            _locMode = mode;
            MyLocationIcon.Data = PathDataConverter.Parse(mode == LocMode.Compass ? Icons.Navigation : Icons.MyLocation);
            MyLocationIcon.Fill = mode == LocMode.None ? ThemeBrush("OnSurfaceVariantBrush") : ThemeBrush("PrimaryBrush");
        }

        private void MyLocation_Click(object sender, RoutedEventArgs e)
        {
            if (!_myPos.HasValue)
            {
                ShowToast("Chưa có địa chỉ mặc định. Mở một địa điểm rồi chọn \"Đặt làm mặc định\", hoặc vào Cài đặt > Địa chỉ mặc định.", 5000);
                return;
            }

            switch (_locMode)
            {
                case LocMode.None:
                    SetLocMode(LocMode.Follow);
                    MarkProgrammaticMove();
                    var ignored = MapView.TrySetViewAsync(_myPos.Value.ToGeopoint(), Math.Max(MapView.ZoomLevel, 16), null, null, MapAnimationKind.Default);
                    break;
                case LocMode.Follow:
                    if (_compass != null && !double.IsNaN(_compassHeading))
                    {
                        SetLocMode(LocMode.Compass);
                        MarkProgrammaticMove();
                        var ig2 = MapView.TrySetViewAsync(_myPos.Value.ToGeopoint(), null, _compassHeading, null, MapAnimationKind.Default);
                    }
                    else
                    {
                        MarkProgrammaticMove();
                        var ig3 = MapView.TrySetViewAsync(_myPos.Value.ToGeopoint(), null, null, null, MapAnimationKind.Default);
                    }
                    break;
                default:
                    SetLocMode(LocMode.Follow);
                    MarkProgrammaticMove();
                    var ig4 = MapView.TrySetViewAsync(_myPos.Value.ToGeopoint(), null, 0, null, MapAnimationKind.Default);
                    break;
            }
        }

        /// <summary>City-level position from Google (IP based) while GPS has no fix.</summary>
        private LatLng? _approxPos;

        /// <summary>Reference point for search bias: user position, else map centre.</summary>
        private LatLng BiasPoint
        {
            get { return _myPos.HasValue ? _myPos.Value : LatLng.From(MapView.Center); }
        }

        private async System.Threading.Tasks.Task LoadApproxLocationAsync()
        {
            try
            {
                var p = await PlacesService.ApproxLocationAsync(System.Threading.CancellationToken.None);
                if (!p.HasValue) { DiagLog.Write("approx location: none"); return; }
                _approxPos = p;
                DiagLog.Write("approx location " + p.Value);
                // first launch (no saved camera) and still no GPS: open the map on the user's city
                if (!_myPos.HasValue && !AppSettings.HasSavedCamera && _screen == Screen.Home)
                {
                    MarkProgrammaticMove();
                    var ignored = MapView.TrySetViewAsync(p.Value.ToGeopoint(), 12, null, null, MapAnimationKind.Default);
                }
            }
            catch (Exception ex) { DiagLog.Write("approx location failed " + ex.Message); }
        }
    }
}
