using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SnCollector.App.Converters;

/// <summary>非空字符串 → Visible，空 → Collapsed</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
