using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ElfLens.Converters;

public static class HighlightConverters
{
    public static readonly IValueConverter HexToBrush = new HexToBrushConverter();

    private class HexToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is string hex && hex.Length > 0)
                return new SolidColorBrush(Color.Parse(hex));
            return new SolidColorBrush(Color.Parse("#B0BEC5"));
        }

        public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}
