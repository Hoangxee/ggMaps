using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Phone.UI.Input;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace GGMaps
{
    public enum Screen { Home, Results, Place, Directions }

    public sealed partial class MainPage : Page
    {
        // ==========================================
        // SHARED STATE (declared only here)
        // ==========================================
        private Screen _screen = Screen.Home;
        private string _searchBarTitle = "";

        // results
        private readonly ObservableCollection<Place> _results = new ObservableCollection<Place>();
        private List<Place> _allResults = new List<Place>();
        private string _lastQuery;

        // search screen
        private readonly ObservableCollection<Suggestion> _suggestions = new ObservableCollection<Suggestion>();

        // directions
        private readonly ObservableCollection<RouteRow> _routeRows = new ObservableCollection<RouteRow>();

        // place
        private Place _place;

        private CancellationTokenSource _toastCts;
        private bool _initialLoadDone;

        // share payload for DataTransferManager.DataRequested
        private string _shareTitle, _shareText;

        public MainPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            ResultsList.ItemsSource = _results;
            SuggestionList.ItemsSource = _suggestions;
            DirRoutesList.ItemsSource = _routeRows;

            InitMap();
            BuildChips();
            InitSearchTimer();
            UpdateChrome();

            // the WP8.1 keyboard covers the page instead of resizing it: shrink the search screen above it
            var pane = InputPane.GetForCurrentView();
            pane.Showing += InputPane_Showing;
            pane.Hiding += InputPane_Hiding;

            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
            Window.Current.VisibilityChanged += Window_VisibilityChanged;
            Application.Current.Suspending += Current_Suspending;
            ApplyStatusBar();
        }

        // ==========================================
        // LIFECYCLE
        // ==========================================

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            try
            {
                var dtm = DataTransferManager.GetForCurrentView();
                dtm.DataRequested -= MainPage_DataRequested;
                dtm.DataRequested += MainPage_DataRequested;
            }
            catch { }

            if (_initialLoadDone) return;
            _initialLoadDone = true;
            var v = Package.Current.Id.Version;
            DiagLog.Write("=== start " + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision + " theme=" + AppSettings.Theme);
            var ignored = ApplyTilesAsync();
            if (!AppSettings.HasSavedCamera) _centerOnFirstFix = true;
            StartLocation();
            StartCompass();
        }

        private void Window_VisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            if (e.Visible)
            {
                StartLocation();
                StartCompass();
            }
            else
            {
                // the Geolocator is kept: stopping it restarted the GPS cold start every time the screen turned off
                StopCompass();
                SaveCamera();
            }
        }

        private void Current_Suspending(object sender, SuspendingEventArgs e)
        {
            SaveCamera();
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SheetBody.Height = e.NewSize.Height;
            SetSheet(_sheetState, false);
        }

        // ==========================================
        // KEYBOARD
        // ==========================================

        private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            if (SearchScreen.Visibility != Visibility.Visible) return;
            args.EnsuredFocusedElementInView = true;   // handled here: no automatic page shift
            SearchScreen.Margin = new Thickness(0, 0, 0, args.OccludedRect.Height);
        }

        private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            SearchScreen.Margin = new Thickness(0);
        }

        // ==========================================
        // BACK KEY
        // ==========================================

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            try { e.Handled = HandleBack(); }
            catch (Exception ex) { DiagLog.Write("back " + ex.Message); e.Handled = true; }
        }

        // ==========================================
        // TOAST (snackbar) + BUSY BAR
        // ==========================================

        private async void ShowToast(string message, int durationMs = 2800)
        {
            var oldCts = _toastCts;
            _toastCts = new CancellationTokenSource();
            if (oldCts != null)
            {
                try { oldCts.Cancel(); } catch { }
            }
            var token = _toastCts.Token;
            try
            {
                ToastFadeOutStoryboard.Stop();
                ToastText.Text = message ?? "";
                ToastNotification.Visibility = Visibility.Visible;
                ToastFadeInStoryboard.Begin();
                await Task.Delay(durationMs, token);
                ToastFadeOutStoryboard.Begin();
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private void ToastFadeOutStoryboard_Completed(object sender, object e)
        {
            ToastNotification.Visibility = Visibility.Collapsed;
        }

        private int _busyCount;

        private void SetBusy(bool busy)
        {
            _busyCount = Math.Max(0, _busyCount + (busy ? 1 : -1));
            BusyBar.Visibility = _busyCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==========================================
        // DIALOGS / LAUNCH / SHARE
        // ==========================================

        private async Task AlertAsync(string title, string message)
        {
            try
            {
                var dialog = new MessageDialog(message ?? "", title ?? "");
                dialog.Commands.Add(new UICommand("OK"));
                await dialog.ShowAsync();
            }
            catch { }
        }

        private async void LaunchUri(string uri)
        {
            try
            {
                if (string.IsNullOrEmpty(uri)) return;
                await Launcher.LaunchUriAsync(new Uri(uri));
            }
            catch (Exception ex)
            {
                ShowToast("Không mở được liên kết: " + ex.Message);
            }
        }

        private void Share(string title, string text)
        {
            _shareTitle = title;
            _shareText = text;
            try { DataTransferManager.ShowShareUI(); }
            catch (Exception ex) { ShowToast("Không chia sẻ được: " + ex.Message); }
        }

        private void MainPage_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (string.IsNullOrEmpty(_shareText)) return;
            var data = args.Request.Data;
            data.Properties.Title = string.IsNullOrEmpty(_shareTitle) ? "GG Maps" : _shareTitle;
            data.SetText(_shareText);
            Uri uri;
            int idx = _shareText.LastIndexOf("https://", StringComparison.Ordinal);
            if (idx >= 0 && Uri.TryCreate(_shareText.Substring(idx).Trim(), UriKind.Absolute, out uri))
            {
                try { data.SetWebLink(uri); } catch { }
            }
        }

        // ==========================================
        // THEME + STATUS BAR
        // ==========================================

        private Color GetThemeColor(string brushKey)
        {
            bool dark = Fmt.IsDark;
            try
            {
                var themes = Application.Current.Resources.ThemeDictionaries;
                string[] dictKeys = dark ? new[] { "Default", "Dark" } : new[] { "Light" };
                foreach (var dictKey in dictKeys)
                {
                    object dictObj;
                    if (themes.TryGetValue(dictKey, out dictObj))
                    {
                        var dict = dictObj as ResourceDictionary;
                        if (dict != null && dict.ContainsKey(brushKey))
                        {
                            var brush = dict[brushKey] as SolidColorBrush;
                            if (brush != null) return brush.Color;
                        }
                    }
                }
            }
            catch { }
            return dark ? Color.FromArgb(255, 0x20, 0x21, 0x24) : Colors.White;
        }

        private Brush ThemeBrush(string key)
        {
            return new SolidColorBrush(GetThemeColor(key));
        }

        private void ApplyStatusBar()
        {
            try
            {
                var statusBar = StatusBar.GetForCurrentView();
                statusBar.BackgroundOpacity = 1;
                statusBar.BackgroundColor = GetThemeColor("SurfaceBrush");
                statusBar.ForegroundColor = GetThemeColor("OnSurfaceVariantBrush");
            }
            catch { }
        }

        private void ApplyViewerStatusBar(bool viewer)
        {
            try
            {
                var statusBar = StatusBar.GetForCurrentView();
                statusBar.BackgroundColor = viewer ? Colors.Black : GetThemeColor("SurfaceBrush");
                statusBar.ForegroundColor = viewer ? Colors.White : GetThemeColor("OnSurfaceVariantBrush");
            }
            catch { }
        }

        /// <summary>Common "overlay card swallows taps" handler (layers / route options cards).</summary>
        private void OverlayCard_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
