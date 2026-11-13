using Windows.ApplicationModel;
using Windows.UI.Xaml;

namespace GGMaps
{
    /// <summary>Settings: map label size, theme, data.</summary>
    public sealed partial class MainPage
    {
        private bool _loadingSettings;

        private void OpenSettings(bool welcome)
        {
            _loadingSettings = true;
            LabelLargeRadio.IsChecked = AppSettings.LabelScale == "scaleFactor2x";
            LabelNormalRadio.IsChecked = AppSettings.LabelScale != "scaleFactor2x";
            string theme = AppSettings.Theme;
            ThemeLightRadio.IsChecked = theme == "light";
            ThemeDarkRadio.IsChecked = theme == "dark";
            ThemeSystemRadio.IsChecked = theme == "system";
            UpdateMyLocationText();
            var v = Package.Current.Id.Version;
            VersionText.Text = "GG Maps " + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
            SettingsScreen.Visibility = Visibility.Visible;
            _loadingSettings = false;
        }

        private void UpdateMyLocationText()
        {
            if (!AppSettings.HasMyLocation)
            {
                MyLocationText.Text = "Chưa đặt.";
                ResetMyLocationButton.Visibility = Visibility.Collapsed;
                return;
            }
            ResetMyLocationButton.Visibility = Visibility.Visible;
            MyLocationText.Text = AppSettings.MyLabel + "\n" +
                AppSettings.MyLat.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                AppSettings.MyLon.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>"Chọn địa chỉ…": search screen in default-address mode (settings close so the search is visible).</summary>
        private void SetMyLocation_Click(object sender, RoutedEventArgs e)
        {
            CloseSettings();
            OpenSearch(SearchPurpose.SetDefault, "");
        }

        private void ResetMyLocation_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.ClearMyLocation();
            StartLocation();
            UpdateMyLocationText();
            ShowToast("Đã xoá địa chỉ mặc định");
        }

        private void CloseSettings()
        {
            SettingsScreen.Visibility = Visibility.Collapsed;
            try { this.Focus(FocusState.Programmatic); } catch { }
        }

        private void SettingsBack_Click(object sender, RoutedEventArgs e)
        {
            CloseSettings();
        }

        private void LabelScale_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            string scale = LabelLargeRadio.IsChecked == true ? "scaleFactor2x" : "scaleFactor1x";
            if (scale == AppSettings.LabelScale) return;
            AppSettings.LabelScale = scale;
            var ignored = ApplyTilesAsync();
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            string theme = ThemeDarkRadio.IsChecked == true ? "dark" : ThemeSystemRadio.IsChecked == true ? "system" : "light";
            if (theme == AppSettings.Theme) return;
            AppSettings.Theme = theme;
            ThemeNote.Visibility = Visibility.Visible;
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            await HistoryStore.ClearAsync();
            ShowToast("Đã xoá lịch sử tìm kiếm");
        }

        private async void ClearSessions_Click(object sender, RoutedEventArgs e)
        {
            await ApplyTilesAsync();
            ShowToast("Đã tải lại bản đồ");
        }
    }
}
