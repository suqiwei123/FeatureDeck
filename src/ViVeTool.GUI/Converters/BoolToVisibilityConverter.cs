using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ViVeTool.GUI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is Visibility visibility && visibility == Visibility.Visible;
    }
}
