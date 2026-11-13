using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    /// <summary>Layers sheet: map type (default / satellite / terrain) and live traffic.</summary>
    public sealed partial class MainPage
    {
        private const string TrafficIcon = "M20 10h-3V8.86c1.72-.45 3-2 3-3.86h-3V4c0-.55-.45-1-1-1H8c-.55 0-1 .45-1 1v1H4c0 1.86 1.28 3.41 3 3.86V10H4c0 1.86 1.28 3.41 3 3.86V15H4c0 1.86 1.28 3.41 3 3.86V20c0 .55.45 1 1 1h8c.55 0 1-.45 1-1v-1.14c1.72-.45 3-2 3-3.86h-3v-1.14c1.72-.45 3-2 3-3.86zm-8 9c-1.11 0-2-.9-2-2s.89-2 2-2c1.1 0 2 .9 2 2s-.89 2-2 2zm0-5c-1.11 0-2-.9-2-2s.89-2 2-2c1.1 0 2 .9 2 2s-.89 2-2 2zm0-5c-1.11 0-2-.9-2-2 0-1.11.89-2 2-2 1.1 0 2 .89 2 2 0 1.1-.89 2-2 2z";

        private void Layers_Click(object sender, RoutedEventArgs e)
        {
            BuildLayerTiles();
            LayersOverlay.Visibility = Visibility.Visible;
        }

        private void LayersClose_Click(object sender, RoutedEventArgs e)
        {
            LayersOverlay.Visibility = Visibility.Collapsed;
        }

        private void LayersScrim_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            LayersOverlay.Visibility = Visibility.Collapsed;
        }

        private void BuildLayerTiles()
        {
            MapTypeGrid.Children.Clear();
            string type = AppSettings.MapType;
            AddLayerTile(MapTypeGrid, 0, "Mặc định", "roadmap", type == "roadmap",
                Color.FromArgb(255, 0xE8, 0xEA, 0xED), Color.FromArgb(255, 0x8A, 0xB4, 0xF8),
                "M20.5 3l-.16.03L15 5.1 9 3 3.36 4.9c-.21.07-.36.25-.36.48V20.5c0 .28.22.5.5.5l.16-.03L9 18.9l6 2.1 5.64-1.9c.21-.07.36-.25.36-.48V3.5c0-.28-.22-.5-.5-.5zM15 19l-6-2.11V5l6 2.11V19z");
            AddLayerTile(MapTypeGrid, 1, "Vệ tinh", "satellite", type == "satellite",
                Color.FromArgb(255, 0x3B, 0x55, 0x3A), Color.FromArgb(255, 0xC5, 0xD8, 0xB5),
                "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z");
            AddLayerTile(MapTypeGrid, 2, "Địa hình", "terrain", type == "terrain",
                Color.FromArgb(255, 0xDD, 0xE6, 0xCF), Color.FromArgb(255, 0x6B, 0x8E, 0x5A),
                "M14 6l-3.75 5 2.85 3.8-1.6 1.2C9.81 13.75 7 10 7 10l-6 8h22L14 6z");

            MapDetailGrid.Children.Clear();
            AddLayerTile(MapDetailGrid, 0, "Giao thông", "traffic", AppSettings.Traffic,
                Color.FromArgb(255, 0xE6, 0xF4, 0xEA), Color.FromArgb(255, 0x1E, 0x8E, 0x3E), TrafficIcon);
        }

        private void AddLayerTile(Grid grid, int column, string label, string key, bool selected, Color bg, Color fg, string icon)
        {
            var preview = new Border
            {
                Width = 72,
                Height = 72,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(bg),
                BorderThickness = new Thickness(selected ? 3 : 1),
                BorderBrush = selected ? ThemeBrush("PrimaryBrush") : ThemeBrush("OutlineVariantBrush"),
                Child = new Path
                {
                    Data = PathDataConverter.Parse(icon),
                    Width = 30,
                    Height = 30,
                    Stretch = Stretch.Uniform,
                    Fill = new SolidColorBrush(fg)
                }
            };
            var text = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontFamily = (FontFamily)Application.Current.Resources["AppFontMedium"],
                Foreground = selected ? ThemeBrush("PrimaryBrush") : ThemeBrush("OnSurfaceVariantBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(preview);
            stack.Children.Add(text);
            var b = new Button { Style = (Style)Resources["BareButton"], Content = stack, Tag = key, HorizontalAlignment = HorizontalAlignment.Center };
            b.Click += LayerTile_Click;
            Grid.SetColumn(b, column);
            grid.Children.Add(b);
        }

        private async void LayerTile_Click(object sender, RoutedEventArgs e)
        {
            string key = (sender as FrameworkElement).Tag as string;
            if (key == "traffic") AppSettings.Traffic = !AppSettings.Traffic;
            else
            {
                if (AppSettings.MapType == key) return;
                AppSettings.MapType = key;
            }
            BuildLayerTiles();
            await ApplyTilesAsync();
        }
    }
}
