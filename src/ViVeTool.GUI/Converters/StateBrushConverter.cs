using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace ViVeTool.GUI.Converters
{
    public class StateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var text = value as string ?? string.Empty;
            return text switch
            {
                "已启用" => Resolve("SystemFillColorSuccessBrush", "#0F6E56"),
                "已禁用" => Resolve("SystemFillColorCriticalBrush", "#A32D2D"),
                _ => Resolve("TextFillColorSecondaryBrush", "#5F5E5A")
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();

        private static Brush Resolve(string resourceKey, string fallbackHex)
        {
            try
            {
                if (Application.Current?.Resources != null &&
                    Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
                    resource is Brush brush)
                    return brush;
            }
            catch
            {
                // 资源尚未就绪时走下面的兜底颜色
            }

            return new SolidColorBrush(ParseHex(fallbackHex));
        }

        private static Windows.UI.Color ParseHex(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = 255, r = 0, g = 0, b = 0;
            if (hex.Length == 8)
            {
                a = System.Convert.ToByte(hex.Substring(0, 2), 16);
                hex = hex.Substring(2);
            }
            if (hex.Length == 6)
            {
                r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            }
            return Windows.UI.Color.FromArgb(a, r, g, b);
        }
    }
}
