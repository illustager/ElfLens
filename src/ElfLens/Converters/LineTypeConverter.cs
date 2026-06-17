using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ElfLens.Core.ViewModels;

namespace ElfLens.Converters;

public static class LineTypeConverters
{
    public static readonly IValueConverter ToBrush = new LineTypeToBrushConverter();

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private class LineTypeToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LineType type)
            {
                return type switch
                {
                    LineType.Function => Brush("#4FC3F7"),
                    LineType.Branch => Brush("#FFB74D"),
                    LineType.Call => Brush("#81C784"),
                    LineType.Ret => Brush("#EF5350"),
                    LineType.Instruction => Brush("#B0BEC5"),
                    _ => Brush("#757575")
                };
            }
            return Brush("#757575");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
