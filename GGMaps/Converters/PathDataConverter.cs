using System;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace GGMaps
{
    /// <summary>Path mini-language string -> Geometry (WinRT has no built-in string->Geometry conversion for bindings).</summary>
    public sealed class PathDataConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return Parse(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }

        /// <summary>A fresh Geometry each call (a Geometry cannot be shared between two Paths in WinRT).</summary>
        public static Geometry Parse(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            try
            {
                var path = (Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data + "'/>");
                var g = path.Data;
                path.Data = null;
                return g;
            }
            catch
            {
                return null;
            }
        }
    }
}
