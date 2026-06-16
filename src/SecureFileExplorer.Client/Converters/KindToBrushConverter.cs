using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SecureFileExplorer.Client.ViewModels;

namespace SecureFileExplorer.Client.Converters;

/// <summary>
/// FileKind から前景ブラシ（既定）/ 淡色ブラシ（parameter="Tint"）を Colors.xaml から解決する。
/// </summary>
public sealed class KindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not FileKind kind) return Application.Current.FindResource("Type.Default");
        var prefix = (parameter as string) == "Tint" ? "Tint" : "Type";
        var key = $"{prefix}.{kind}";
        return Application.Current.TryFindResource(key)
               ?? Application.Current.FindResource("Type.Default");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
