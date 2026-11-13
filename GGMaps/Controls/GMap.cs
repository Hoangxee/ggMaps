using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    public sealed class MapTapArgs
    {
        public Geopoint Location { get; internal set; }
        public Point Position { get; internal set; }
    }

    /// <summary>
    /// Self-drawn slippy map (Web Mercator, 256 logical px tiles) replacing Windows.UI.Xaml.Controls.Maps.MapControl,
    /// which does not render at all on current Windows Phone 8.1 devices (its map service is gone).
    /// Mirrors the subset of the MapControl API the app uses: Center, ZoomLevel, Heading, TrySetViewAsync,
    /// TrySetViewBoundsAsync, GetLocationFromOffset, MapElements (MapPolyline / MapPolygon), markers with
    /// SetLocation / SetNormalizedAnchorPoint, MapTapped / MapHolding / CenterChanged / ZoomLevelChanged / HeadingChanged.
    ///
    /// Each layer (tiles of one zoom level, vector shapes) holds its content in its own local pixel space and is moved
    /// with one RenderTransform (scale → translate → rotate), so panning / zooming only updates a few transforms.
    /// </summary>
    public sealed class GMap : Grid
    {
        // ------------------------------------------------------------------ attached properties for markers

        public static readonly DependencyProperty LocationProperty =
            DependencyProperty.RegisterAttached("Location", typeof(Geopoint), typeof(GMap), new PropertyMetadata(null, OnMarkerPropertyChanged));

        public static readonly DependencyProperty NormalizedAnchorPointProperty =
            DependencyProperty.RegisterAttached("NormalizedAnchorPoint", typeof(Point), typeof(GMap), new PropertyMetadata(new Point(0, 0), OnMarkerPropertyChanged));

        public static void SetLocation(DependencyObject element, Geopoint value) { element.SetValue(LocationProperty, value); }
        public static Geopoint GetLocation(DependencyObject element) { return (Geopoint)element.GetValue(LocationProperty); }
        public static void SetNormalizedAnchorPoint(DependencyObject element, Point value) { element.SetValue(NormalizedAnchorPointProperty, value); }
        public static Point GetNormalizedAnchorPoint(DependencyObject element) { return (Point)element.GetValue(NormalizedAnchorPointProperty); }

        private static void OnMarkerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return;
            var map = fe.Parent is Canvas ? ((Canvas)fe.Parent).Parent as GMap : null;
            if (map != null) map.PositionMarker(fe);
        }

        // ------------------------------------------------------------------ public surface

        public event TypedEventHandler<GMap, MapTapArgs> MapTapped;
        public event TypedEventHandler<GMap, MapTapArgs> MapHolding;
        public event TypedEventHandler<GMap, object> CenterChanged;
        public event TypedEventHandler<GMap, object> ZoomLevelChanged;
        public event TypedEventHandler<GMap, object> HeadingChanged;

        public ObservableCollection<FrameworkElement> Markers { get; private set; }
        public ObservableCollection<MapElement> MapElements { get; private set; }

        public double MinZoomLevel = 2;
        public double MaxZoomLevel = 21;

        // camera: centre in world pixels at zoom 0 (0..256), fractional zoom, heading in degrees (top of the map = heading)
        private double _cx = 128, _cy = 128, _zoom = 3, _heading;

        public Geopoint Center
        {
            get { return new Geopoint(new BasicGeoposition { Latitude = YToLat(_cy), Longitude = XToLon(_cx) }); }
            set
            {
                if (value == null) return;
                SetCamera(LonToX(value.Position.Longitude), LatToY(value.Position.Latitude), _zoom, _heading);
            }
        }

        public double ZoomLevel
        {
            get { return _zoom; }
            set { SetCamera(_cx, _cy, value, _heading); }
        }

        public double Heading
        {
            get { return _heading; }
            set { SetCamera(_cx, _cy, _zoom, value); }
        }

        /// <summary>Tile URL for (x, y, z); null = no tiles. Changing it reloads every tile.</summary>
        public void SetTileSource(Func<int, int, int, string> urlForTile, string cacheKey)
        {
            _tileUrl = urlForTile;
            _tileKey = cacheKey ?? "";
            ClearTiles();
            Invalidate();
        }

        // ------------------------------------------------------------------ internals

        private Func<int, int, int, string> _tileUrl;
        private string _tileKey = "";
        private readonly Canvas _shapeCanvas = new Canvas { IsHitTestVisible = false };
        private readonly Canvas _markerCanvas = new Canvas();
        private TileLayer _low, _high;
        private readonly RectangleGeometry _clip = new RectangleGeometry();

        // shapes are stored in level-16 pixels relative to _shapeOrigin (keeps float coordinates small)
        private const int ShapeLevel = 16;
        private double _shapeOx = double.NaN, _shapeOy;
        private readonly List<ShapeItem> _shapes = new List<ShapeItem>();
        private readonly TransformGroup _shapeTransform;
        private readonly ScaleTransform _shapeScale = new ScaleTransform();
        private readonly TranslateTransform _shapeTranslate = new TranslateTransform();
        private readonly RotateTransform _shapeRotate = new RotateTransform();

        private bool _dirty, _renderingHooked;
        private readonly double _rawScale;

        // bitmap cache (url -> image), LRU
        private readonly Dictionary<string, BitmapImage> _bitmaps = new Dictionary<string, BitmapImage>();
        private readonly LinkedList<string> _lru = new LinkedList<string>();
        private const int MaxBitmaps = 160;
        private readonly Dictionary<string, DateTime> _failed = new Dictionary<string, DateTime>();

        public GMap()
        {
            Markers = new ObservableCollection<FrameworkElement>();
            MapElements = new ObservableCollection<MapElement>();
            Markers.CollectionChanged += Markers_CollectionChanged;
            MapElements.CollectionChanged += MapElements_CollectionChanged;

            _shapeTransform = new TransformGroup();
            _shapeTransform.Children.Add(_shapeScale);
            _shapeTransform.Children.Add(_shapeTranslate);
            _shapeTransform.Children.Add(_shapeRotate);
            _shapeCanvas.RenderTransform = _shapeTransform;

            Canvas.SetZIndex(_shapeCanvas, 2);
            Canvas.SetZIndex(_markerCanvas, 3);
            Children.Add(_shapeCanvas);
            Children.Add(_markerCanvas);
            Clip = _clip;
            if (Background == null) Background = new SolidColorBrush(Color.FromArgb(255, 0xE8, 0xEA, 0xED));

            try { _rawScale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel; } catch { _rawScale = 1.6; }
            if (_rawScale <= 0) _rawScale = 1.6;

            ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale |
                               ManipulationModes.Rotate | ManipulationModes.TranslateInertia;
            ManipulationStarted += OnManipulationStarted;
            ManipulationDelta += OnManipulationDelta;
            ManipulationInertiaStarting += OnInertiaStarting;
            ManipulationCompleted += OnManipulationCompleted;
            Tapped += OnTapped;
            DoubleTapped += OnDoubleTapped;
            Holding += OnHolding;
            IsDoubleTapEnabled = true;
            IsHoldingEnabled = true;
            SizeChanged += (s, e) =>
            {
                _clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
                Invalidate();
            };
        }

        // ------------------------------------------------------------------ projection

        private const double MaxLat = 85.05112878;

        public static double LonToX(double lon) { return (lon + 180.0) / 360.0 * 256.0; }

        public static double LatToY(double lat)
        {
            lat = Math.Max(-MaxLat, Math.Min(MaxLat, lat));
            double s = Math.Sin(lat * Math.PI / 180.0);
            return (0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI)) * 256.0;
        }

        public static double XToLon(double x) { return x / 256.0 * 360.0 - 180.0; }

        public static double YToLat(double y)
        {
            double n = Math.PI - 2.0 * Math.PI * y / 256.0;
            return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        }

        private double W { get { return ActualWidth > 0 ? ActualWidth : 480; } }
        private double H { get { return ActualHeight > 0 ? ActualHeight : 800; } }

        private Point WorldToScreen(double wx, double wy)
        {
            double s = Math.Pow(2, _zoom);
            double dx = (wx - _cx) * s, dy = (wy - _cy) * s;
            double a = -_heading * Math.PI / 180.0;
            double c = Math.Cos(a), sn = Math.Sin(a);
            return new Point(W / 2 + dx * c - dy * sn, H / 2 + dx * sn + dy * c);
        }

        private void ScreenToWorld(Point p, double zoom, double cx, double cy, double heading, out double wx, out double wy)
        {
            double dx = p.X - W / 2, dy = p.Y - H / 2;
            double a = heading * Math.PI / 180.0;
            double c = Math.Cos(a), sn = Math.Sin(a);
            double rx = dx * c - dy * sn, ry = dx * sn + dy * c;
            double s = Math.Pow(2, zoom);
            wx = cx + rx / s;
            wy = cy + ry / s;
        }

        public void GetLocationFromOffset(Point offset, out Geopoint location)
        {
            double wx, wy;
            ScreenToWorld(offset, _zoom, _cx, _cy, _heading, out wx, out wy);
            location = new Geopoint(new BasicGeoposition { Latitude = YToLat(wy), Longitude = XToLon(Wrap(wx)) });
        }

        public void GetOffsetFromLocation(Geopoint location, out Point offset)
        {
            offset = WorldToScreen(LonToX(location.Position.Longitude), LatToY(location.Position.Latitude));
        }

        private static double Wrap(double wx)
        {
            wx %= 256.0;
            if (wx < 0) wx += 256.0;
            return wx;
        }

        // ------------------------------------------------------------------ camera

        private void SetCamera(double cx, double cy, double zoom, double heading)
        {
            zoom = Math.Max(MinZoomLevel, Math.Min(MaxZoomLevel, zoom));
            cy = Math.Max(0, Math.Min(256, cy));
            cx = Wrap(cx);
            heading %= 360.0;
            if (heading < 0) heading += 360.0;

            bool centerChanged = Math.Abs(cx - _cx) > 1e-12 || Math.Abs(cy - _cy) > 1e-12;
            bool zoomChanged = Math.Abs(zoom - _zoom) > 1e-9;
            bool headingChanged = Math.Abs(heading - _heading) > 1e-9;
            _cx = cx; _cy = cy; _zoom = zoom; _heading = heading;
            if (!centerChanged && !zoomChanged && !headingChanged) return;
            Invalidate();
            if (centerChanged && CenterChanged != null) CenterChanged(this, null);
            if (zoomChanged && ZoomLevelChanged != null) ZoomLevelChanged(this, null);
            if (headingChanged && HeadingChanged != null) HeadingChanged(this, null);
        }

        // ---------------- animation

        private bool _animating;
        private DateTime _animStart;
        private double _animMs;
        private double _fx, _fy, _fz, _fh, _tx, _ty, _tz, _th;
        private TaskCompletionSource<bool> _animTcs;

        public Task<bool> TrySetViewAsync(Geopoint center, double? zoomLevel, double? heading, double? desiredPitch, MapAnimationKind animation)
        {
            double tx = center != null ? LonToX(center.Position.Longitude) : _cx;
            double ty = center != null ? LatToY(center.Position.Latitude) : _cy;
            double tz = zoomLevel.HasValue ? Math.Max(MinZoomLevel, Math.Min(MaxZoomLevel, zoomLevel.Value)) : _zoom;
            double th = heading.HasValue ? heading.Value : _heading;
            return AnimateTo(tx, ty, tz, th, animation);
        }

        public Task<bool> TrySetViewBoundsAsync(GeoboundingBox bounds, Thickness? margin, MapAnimationKind animation)
        {
            var m = margin ?? new Thickness(0);
            double x1 = LonToX(bounds.NorthwestCorner.Longitude), x2 = LonToX(bounds.SoutheastCorner.Longitude);
            double y1 = LatToY(bounds.NorthwestCorner.Latitude), y2 = LatToY(bounds.SoutheastCorner.Latitude);
            if (x2 < x1) x2 += 256;
            double bw = Math.Max(1e-9, x2 - x1), bh = Math.Max(1e-9, Math.Abs(y2 - y1));
            double aw = Math.Max(40, W - m.Left - m.Right), ah = Math.Max(40, H - m.Top - m.Bottom);
            double zoom = Math.Log(Math.Min(aw / bw, ah / bh), 2);
            zoom = Math.Max(MinZoomLevel, Math.Min(Math.Min(MaxZoomLevel, 18), zoom));

            // the box centre must land in the middle of the free area, not of the screen
            double bcx = (x1 + x2) / 2, bcy = (y1 + y2) / 2;
            double sx = m.Left + aw / 2 - W / 2, sy = m.Top + ah / 2 - H / 2;   // screen offset of the free-area centre
            double a = _heading * Math.PI / 180.0;
            double rx = sx * Math.Cos(a) - sy * Math.Sin(a), ry = sx * Math.Sin(a) + sy * Math.Cos(a);
            double s = Math.Pow(2, zoom);
            return AnimateTo(bcx - rx / s, bcy - ry / s, zoom, _heading, animation);
        }

        private Task<bool> AnimateTo(double tx, double ty, double tz, double th, MapAnimationKind kind)
        {
            StopAnimation(false);
            if (kind == MapAnimationKind.None || ActualWidth <= 0)
            {
                SetCamera(tx, ty, tz, th);
                return Task.FromResult(true);
            }
            // shortest way around the world / compass
            if (tx - _cx > 128) tx -= 256; else if (_cx - tx > 128) tx += 256;
            double dh = (th - _heading) % 360;
            if (dh > 180) dh -= 360; if (dh < -180) dh += 360;

            _fx = _cx; _fy = _cy; _fz = _zoom; _fh = _heading;
            _tx = tx; _ty = ty; _tz = tz; _th = _heading + dh;
            double dist = Math.Abs(tz - _zoom) + Math.Min(4, Math.Sqrt((tx - _cx) * (tx - _cx) + (ty - _cy) * (ty - _cy)) * Math.Pow(2, Math.Min(_zoom, tz)) / Math.Max(W, H));
            _animMs = kind == MapAnimationKind.Linear ? 250 : Math.Min(700, 300 + 80 * dist);
            _animStart = DateTime.Now;
            _animTcs = new TaskCompletionSource<bool>();
            _animating = true;
            HookRendering();
            return _animTcs.Task;
        }

        private void StopAnimation(bool completed)
        {
            if (!_animating) return;
            _animating = false;
            var tcs = _animTcs;
            _animTcs = null;
            if (tcs != null) tcs.TrySetResult(completed);
        }

        private void StepAnimation()
        {
            double t = (DateTime.Now - _animStart).TotalMilliseconds / _animMs;
            if (t >= 1)
            {
                SetCamera(_tx, _ty, _tz, _th);
                StopAnimation(true);
                return;
            }
            double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;   // ease-in-out cubic
            SetCamera(_fx + (_tx - _fx) * e, _fy + (_ty - _fy) * e, _fz + (_tz - _fz) * e, _fh + (_th - _fh) * e);
        }

        // ------------------------------------------------------------------ gestures

        private double _rotationAccum;
        private bool _rotating;

        private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            StopAnimation(false);
            _rotationAccum = 0;
            _rotating = false;
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var d = e.Delta;
            var pos = e.Position;
            double cx = _cx, cy = _cy, zoom = _zoom, heading = _heading;

            // pinch zoom around the fingers
            if (Math.Abs(d.Scale - 1) > 1e-4 && d.Scale > 0)
            {
                double bx, by, ax, ay;
                ScreenToWorld(pos, zoom, cx, cy, heading, out bx, out by);
                zoom = Math.Max(MinZoomLevel, Math.Min(MaxZoomLevel, zoom + Math.Log(d.Scale, 2)));
                ScreenToWorld(pos, zoom, cx, cy, heading, out ax, out ay);
                cx += bx - ax; cy += by - ay;
            }

            // two-finger rotation, only after a clear twist (pinching alone should not rotate)
            _rotationAccum += d.Rotation;
            if (!_rotating && Math.Abs(_rotationAccum) > 14) { _rotating = true; }
            if (_rotating && Math.Abs(d.Rotation) > 1e-4)
            {
                double bx, by, ax, ay;
                ScreenToWorld(pos, zoom, cx, cy, heading, out bx, out by);
                heading -= d.Rotation;
                ScreenToWorld(pos, zoom, cx, cy, heading, out ax, out ay);
                cx += bx - ax; cy += by - ay;
            }

            // pan
            if (d.Translation.X != 0 || d.Translation.Y != 0)
            {
                double a = heading * Math.PI / 180.0;
                double tx = d.Translation.X, ty = d.Translation.Y;
                double rx = tx * Math.Cos(a) - ty * Math.Sin(a), ry = tx * Math.Sin(a) + ty * Math.Cos(a);
                double s = Math.Pow(2, zoom);
                cx -= rx / s; cy -= ry / s;
            }
            SetCamera(cx, cy, zoom, heading);
            e.Handled = true;
        }

        private void OnInertiaStarting(object sender, ManipulationInertiaStartingRoutedEventArgs e)
        {
            e.TranslationBehavior.DesiredDeceleration = 0.0025;   // px / ms²
        }

        private void OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            Invalidate();
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.Handled) return;
            var p = e.GetPosition(this);
            Geopoint g;
            GetLocationFromOffset(p, out g);
            if (MapTapped != null) MapTapped(this, new MapTapArgs { Location = g, Position = p });
        }

        private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.Handled) return;
            var p = e.GetPosition(this);
            double wx, wy;
            ScreenToWorld(p, _zoom, _cx, _cy, _heading, out wx, out wy);
            double nz = Math.Min(MaxZoomLevel, _zoom + 1);
            double f = Math.Pow(2, _zoom - nz);
            // keep the tapped point under the finger
            AnimateTo(wx + (_cx - wx) * f, wy + (_cy - wy) * f, nz, _heading, MapAnimationKind.Default);
            e.Handled = true;
        }

        private void OnHolding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Windows.UI.Input.HoldingState.Started || e.Handled) return;
            var p = e.GetPosition(this);
            Geopoint g;
            GetLocationFromOffset(p, out g);
            if (MapHolding != null) MapHolding(this, new MapTapArgs { Location = g, Position = p });
        }

        // ------------------------------------------------------------------ render loop

        private void Invalidate()
        {
            _dirty = true;
            HookRendering();
        }

        private void HookRendering()
        {
            if (_renderingHooked) return;
            _renderingHooked = true;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object sender, object e)
        {
            if (_animating) StepAnimation();
            if (_dirty)
            {
                _dirty = false;
                try { UpdateAll(); } catch (Exception ex) { DiagLog.Write("GMap render " + ex.Message); }
            }
            if (!_animating && !_dirty)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingHooked = false;
            }
        }

        private void UpdateAll()
        {
            UpdateTiles();
            UpdateShapes();
            foreach (var m in Markers) PositionMarker(m);
        }

        // ------------------------------------------------------------------ tiles

        private sealed class TileLayer
        {
            public int Level;
            public double Ox, Oy;   // origin in level pixels
            public Canvas Canvas;
            public readonly Dictionary<long, Image> Tiles = new Dictionary<long, Image>();
            public readonly ScaleTransform Scale = new ScaleTransform();
            public readonly TranslateTransform Translate = new TranslateTransform();
            public readonly RotateTransform Rotate = new RotateTransform();

            public TileLayer(Canvas canvas, int level, double cxLevel, double cyLevel)
            {
                Canvas = canvas;
                Level = level;
                Ox = Math.Floor(cxLevel / 256) * 256;
                Oy = Math.Floor(cyLevel / 256) * 256;
                var g = new TransformGroup();
                g.Children.Add(Scale);
                g.Children.Add(Translate);
                g.Children.Add(Rotate);
                canvas.RenderTransform = g;
            }
        }

        private TileLayer NewLayer(int level)
        {
            var canvas = new Canvas { IsHitTestVisible = false };
            double ls = Math.Pow(2, level);
            var layer = new TileLayer(canvas, level, _cx * ls, _cy * ls);
            Children.Insert(0, canvas);
            return layer;
        }

        private void RemoveLayer(TileLayer layer)
        {
            if (layer != null) Children.Remove(layer.Canvas);
        }

        private void ClearTiles()
        {
            RemoveLayer(_low);
            RemoveLayer(_high);
            _low = _high = null;
        }

        private void UpdateTiles()
        {
            if (_tileUrl == null) return;
            int level = (int)Math.Round(_zoom);
            level = Math.Max(1, Math.Min(21, level));

            if (_high == null || _high.Level != level)
            {
                if (_high != null && _high.Level == level - 1)
                {
                    // zooming in: the current level stays underneath as a placeholder
                    RemoveLayer(_low);
                    _low = _high;
                    _high = NewLayer(level);
                }
                else if (_low != null && _low.Level == level)
                {
                    // zooming out: the placeholder level becomes the main one
                    RemoveLayer(_high);
                    _high = _low;
                    _low = null;
                }
                else
                {
                    ClearTiles();
                    _high = NewLayer(level);
                }
                Canvas.SetZIndex(_high.Canvas, 1);
                if (_low != null) Canvas.SetZIndex(_low.Canvas, 0);
            }
            // the lower level only keeps what it already has (no extra downloads)
            if (_low != null) UpdateLayer(_low, 0, false);
            UpdateLayer(_high, 1, true);
        }

        private void UpdateLayer(TileLayer layer, int margin, bool addNew)
        {
            double levelScale = Math.Pow(2, layer.Level);
            double s = Math.Pow(2, _zoom - layer.Level);
            double ccx = _cx * levelScale, ccy = _cy * levelScale;

            layer.Scale.ScaleX = layer.Scale.ScaleY = s;
            layer.Translate.X = (layer.Ox - ccx) * s + W / 2;
            layer.Translate.Y = (layer.Oy - ccy) * s + H / 2;
            layer.Rotate.Angle = -_heading;
            layer.Rotate.CenterX = W / 2;
            layer.Rotate.CenterY = H / 2;

            // visible area (rotation-safe: bounding box of the four corners) in level pixels
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var corner in new[] { new Point(0, 0), new Point(W, 0), new Point(0, H), new Point(W, H) })
            {
                double wx, wy;
                ScreenToWorld(corner, _zoom, _cx, _cy, _heading, out wx, out wy);
                wx *= levelScale; wy *= levelScale;
                minX = Math.Min(minX, wx); maxX = Math.Max(maxX, wx);
                minY = Math.Min(minY, wy); maxY = Math.Max(maxY, wy);
            }
            int n = 1 << layer.Level;
            int tx0 = (int)Math.Floor(minX / 256) - margin, tx1 = (int)Math.Floor(maxX / 256) + margin;
            int ty0 = Math.Max(0, (int)Math.Floor(minY / 256) - margin), ty1 = Math.Min(n - 1, (int)Math.Floor(maxY / 256) + margin);
            if (tx1 - tx0 > 40 || ty1 - ty0 > 40) return;   // safety while the layout is not ready

            var wanted = new HashSet<long>();
            for (int ty = ty0; ty <= ty1; ty++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    long key = ((long)tx << 32) ^ (uint)ty;
                    wanted.Add(key);
                    if (layer.Tiles.ContainsKey(key) || !addNew) continue;
                    int wrapped = ((tx % n) + n) % n;
                    string url = _tileUrl(wrapped, ty, layer.Level);
                    if (url == null) continue;
                    DateTime failedAt;
                    if (_failed.TryGetValue(url, out failedAt) && (DateTime.Now - failedAt).TotalSeconds < 8) continue;
                    var img = new Image { Width = 256, Height = 256, Stretch = Stretch.Fill, Source = GetBitmap(url) };
                    Canvas.SetLeft(img, tx * 256 - layer.Ox);
                    Canvas.SetTop(img, ty * 256 - layer.Oy);
                    layer.Tiles[key] = img;
                    layer.Canvas.Children.Add(img);
                }
            }
            // drop tiles that left the screen
            if (layer.Tiles.Count > wanted.Count)
            {
                var gone = new List<long>();
                foreach (var k in layer.Tiles.Keys) if (!wanted.Contains(k)) gone.Add(k);
                foreach (var k in gone)
                {
                    layer.Canvas.Children.Remove(layer.Tiles[k]);
                    layer.Tiles.Remove(k);
                }
            }
        }

        private BitmapImage GetBitmap(string url)
        {
            BitmapImage bmp;
            string key = _tileKey + url;
            if (_bitmaps.TryGetValue(key, out bmp))
            {
                _lru.Remove(key);
                _lru.AddFirst(key);
                return bmp;
            }
            bmp = new BitmapImage { DecodePixelWidth = (int)Math.Round(256 * _rawScale) };
            bmp.ImageFailed += (s, e) =>
            {
                _failed[url] = DateTime.Now;
                _bitmaps.Remove(key);
                _lru.Remove(key);
                DiagLog.Write("tile failed " + e.ErrorMessage + " " + url);
            };
            bmp.UriSource = new Uri(url);
            _bitmaps[key] = bmp;
            _lru.AddFirst(key);
            while (_lru.Count > MaxBitmaps)
            {
                string last = _lru.Last.Value;
                _lru.RemoveLast();
                _bitmaps.Remove(last);
            }
            return bmp;
        }

        // ------------------------------------------------------------------ markers

        private void Markers_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _markerCanvas.Children.Clear();
                return;
            }
            if (e.OldItems != null)
                foreach (FrameworkElement fe in e.OldItems) { _markerCanvas.Children.Remove(fe); fe.SizeChanged -= Marker_SizeChanged; }
            if (e.NewItems != null)
            {
                foreach (FrameworkElement fe in e.NewItems)
                {
                    if (!_markerCanvas.Children.Contains(fe)) _markerCanvas.Children.Add(fe);
                    fe.SizeChanged += Marker_SizeChanged;
                    PositionMarker(fe);
                }
            }
        }

        private void Marker_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PositionMarker(sender as FrameworkElement);
        }

        private void PositionMarker(FrameworkElement fe)
        {
            if (fe == null) return;
            var loc = GetLocation(fe);
            if (loc == null) { fe.Visibility = Visibility.Collapsed; return; }
            var p = WorldToScreen(NearestCopyX(LonToX(loc.Position.Longitude)), LatToY(loc.Position.Latitude));
            var anchor = GetNormalizedAnchorPoint(fe);
            double w = fe.ActualWidth > 0 ? fe.ActualWidth : (!double.IsNaN(fe.Width) ? fe.Width : 0);
            double h = fe.ActualHeight > 0 ? fe.ActualHeight : (!double.IsNaN(fe.Height) ? fe.Height : 0);
            Canvas.SetLeft(fe, p.X - anchor.X * w);
            Canvas.SetTop(fe, p.Y - anchor.Y * h);
        }

        /// <summary>World x of the copy of the world nearest to the camera (antimeridian).</summary>
        private double NearestCopyX(double wx)
        {
            if (wx - _cx > 128) return wx - 256;
            if (_cx - wx > 128) return wx + 256;
            return wx;
        }

        // ------------------------------------------------------------------ shapes (MapPolyline / MapPolygon)

        private sealed class ShapeItem
        {
            public MapElement Element;
            public Shape Shape;
            public double Thickness;
        }

        private void MapElements_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _shapes.Clear();
                _shapeCanvas.Children.Clear();
                _shapeOx = double.NaN;
                return;
            }
            if (e.OldItems != null)
            {
                foreach (MapElement el in e.OldItems)
                {
                    var item = _shapes.Find(x => x.Element == el);
                    if (item == null) continue;
                    _shapes.Remove(item);
                    _shapeCanvas.Children.Remove(item.Shape);
                }
            }
            if (e.NewItems != null)
                foreach (MapElement el in e.NewItems) AddShape(el);
            if (_shapes.Count == 0) _shapeOx = double.NaN;
            Invalidate();
        }

        private void AddShape(MapElement el)
        {
            IReadOnlyList<BasicGeoposition> path = null;
            Shape shape = null;
            double thickness = 1;
            var line = el as MapPolyline;
            var poly = el as MapPolygon;
            if (line != null && line.Path != null)
            {
                path = line.Path.Positions;
                thickness = line.StrokeThickness;
                var pl = new Polyline
                {
                    Stroke = new SolidColorBrush(line.StrokeColor),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                if (line.StrokeDashed)
                {
                    pl.StrokeDashArray = new DoubleCollection { 0.1, 2 };
                    pl.StrokeDashCap = PenLineCap.Round;
                }
                shape = pl;
            }
            else if (poly != null && poly.Path != null)
            {
                path = poly.Path.Positions;
                thickness = poly.StrokeThickness;
                shape = new Polygon { Fill = new SolidColorBrush(poly.FillColor), Stroke = new SolidColorBrush(poly.StrokeColor) };
            }
            if (shape == null || path == null || path.Count == 0) return;

            double ls = Math.Pow(2, ShapeLevel);
            if (double.IsNaN(_shapeOx))
            {
                _shapeOx = LonToX(path[0].Longitude) * ls;
                _shapeOy = LatToY(path[0].Latitude) * ls;
            }
            var pts = new PointCollection();
            foreach (var g in path)
                pts.Add(new Point(LonToX(g.Longitude) * ls - _shapeOx, LatToY(g.Latitude) * ls - _shapeOy));
            if (shape is Polyline) ((Polyline)shape).Points = pts; else ((Polygon)shape).Points = pts;

            Canvas.SetZIndex(shape, el.ZIndex);
            _shapes.Add(new ShapeItem { Element = el, Shape = shape, Thickness = thickness });
            _shapeCanvas.Children.Add(shape);
        }

        private void UpdateShapes()
        {
            if (_shapes.Count == 0 || double.IsNaN(_shapeOx)) return;
            double ls = Math.Pow(2, ShapeLevel);
            double s = Math.Pow(2, _zoom - ShapeLevel);
            _shapeScale.ScaleX = _shapeScale.ScaleY = s;
            _shapeTranslate.X = (_shapeOx - _cx * ls) * s + W / 2;
            _shapeTranslate.Y = (_shapeOy - _cy * ls) * s + H / 2;
            _shapeRotate.Angle = -_heading;
            _shapeRotate.CenterX = W / 2;
            _shapeRotate.CenterY = H / 2;
            foreach (var item in _shapes)
            {
                double t = item.Thickness / s;
                if (Math.Abs(item.Shape.StrokeThickness - t) > t * 0.01) item.Shape.StrokeThickness = t;
            }
        }
    }
}
