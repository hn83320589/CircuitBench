using System.Globalization;
using Avalonia.Data.Converters;
using CircuitBench.App.ViewModels;

namespace CircuitBench.App.Converters;

/// <summary>Page 列舉 ↔ TabControl 的索引。</summary>
public sealed class PageToIndexConverter : IValueConverter
{
    public static PageToIndexConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Page p ? (int)p : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (Page)i : Page.Editor;
}
