using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CodexBarWin.App.Converters;

/// <summary>Visible when the bound bool is false (used for "no data" fallback panels).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
