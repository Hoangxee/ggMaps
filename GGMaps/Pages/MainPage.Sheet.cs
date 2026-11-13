using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace GGMaps
{
    public enum SheetState { Hidden, Peek, Half, Full }

    /// <summary>
    /// iOS-style bottom sheet: SheetBody is as tall as the page and slides with SheetTransform.Y = H - visible.
    /// FabRow (my location / directions / attribution) rides on top of it with FabTransform.Y = -visible.
    /// </summary>
    public sealed partial class MainPage
    {
        private SheetState _sheetState = SheetState.Hidden;
        private double _peekHeight = 160;
        private double _dragStartY;
        private bool _sheetAnimating;
        private double _animFrom, _animTo;
        private DateTime _animStart;
        private const double AnimMs = 240;

        private double PageHeight { get { double h = LayoutRoot.ActualHeight; return h > 0 ? h : 640; } }

        /// <summary>Space at the top the full sheet must not cover (directions header).</summary>
        private double SheetTopInset
        {
            get { return _screen == Screen.Directions && DirectionsHeader.ActualHeight > 0 ? DirectionsHeader.ActualHeight : 0; }
        }

        private double VisibleHeightFor(SheetState s)
        {
            double h = PageHeight;
            switch (s)
            {
                case SheetState.Peek: return Math.Min(_peekHeight, h - SheetTopInset);
                case SheetState.Half: return Math.Max(_peekHeight, Math.Round(h * 0.5));
                case SheetState.Full: return h - SheetTopInset;
                default: return 0;
            }
        }

        /// <summary>Visible sheet height right now (used for camera padding).</summary>
        private double SheetVisibleHeight
        {
            get
            {
                if (_sheetAnimating) return Math.Max(0, PageHeight - _animTo);
                return Math.Max(0, PageHeight - SheetTransform.Y);
            }
        }

        private void SetSheet(SheetState s, bool animate = true)
        {
            _sheetState = s;
            double target = PageHeight - VisibleHeightFor(s);
            if (animate) AnimateSheetTo(target);
            else
            {
                StopSheetAnimation();
                ApplySheetY(target);
            }
        }

        private void ApplySheetY(double y)
        {
            SheetTransform.Y = y;
            double visible = PageHeight - y;
            FabTransform.Y = -visible;
            // floating buttons fade away when the sheet covers most of the map
            double fade = visible > PageHeight * 0.55 ? 0 : 1;
            FabRow.Opacity = fade;
            FabRow.IsHitTestVisible = fade > 0;
        }

        private void AnimateSheetTo(double target)
        {
            StopSheetAnimation();
            _animFrom = SheetTransform.Y;
            _animTo = target;
            if (Math.Abs(_animFrom - _animTo) < 1)
            {
                ApplySheetY(target);
                return;
            }
            _animStart = DateTime.Now;
            _sheetAnimating = true;
            CompositionTarget.Rendering += SheetAnimation_Rendering;
        }

        private void StopSheetAnimation()
        {
            if (!_sheetAnimating) return;
            _sheetAnimating = false;
            CompositionTarget.Rendering -= SheetAnimation_Rendering;
        }

        private void SheetAnimation_Rendering(object sender, object e)
        {
            double t = (DateTime.Now - _animStart).TotalMilliseconds / AnimMs;
            if (t >= 1)
            {
                StopSheetAnimation();
                ApplySheetY(_animTo);
                return;
            }
            double eased = 1 - Math.Pow(1 - t, 3);   // ease-out cubic
            ApplySheetY(_animFrom + (_animTo - _animFrom) * eased);
        }

        // ------------------------------------------------------------ dragging

        private void Sheet_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            StopSheetAnimation();
            _dragStartY = SheetTransform.Y;
        }

        private void Sheet_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            double h = PageHeight;
            double minY = h - VisibleHeightFor(SheetState.Full);
            double maxY = h - (_screen == Screen.Home ? 0 : Math.Min(_peekHeight, 90));
            double y = _dragStartY + e.Cumulative.Translation.Y;
            ApplySheetY(Math.Max(minY, Math.Min(maxY, y)));
            e.Handled = true;
        }

        private void Sheet_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            double visible = PageHeight - SheetTransform.Y;
            double v = e.Velocities.Linear.Y;   // px / ms, > 0 = downward
            SheetState[] order = { SheetState.Peek, SheetState.Half, SheetState.Full };

            int nearest = 0;
            double best = double.MaxValue;
            for (int i = 0; i < order.Length; i++)
            {
                double d = Math.Abs(VisibleHeightFor(order[i]) - visible);
                if (d < best) { best = d; nearest = i; }
            }
            int target = nearest;
            if (v < -0.4)
            {
                // flick up: next state above the current position
                target = order.Length - 1;
                for (int i = 0; i < order.Length; i++) if (VisibleHeightFor(order[i]) > visible + 1) { target = i; break; }
            }
            else if (v > 0.4)
            {
                target = 0;
                for (int i = order.Length - 1; i >= 0; i--) if (VisibleHeightFor(order[i]) < visible - 1) { target = i; break; }
            }
            SetSheet(order[target]);
            e.Handled = true;
        }

        private void SheetHandle_Tapped(object sender, TappedRoutedEventArgs e)
        {
            switch (_sheetState)
            {
                case SheetState.Peek: SetSheet(SheetState.Half); break;
                case SheetState.Half: SetSheet(SheetState.Full); break;
                default: SetSheet(SheetState.Peek); break;
            }
        }
    }
}
