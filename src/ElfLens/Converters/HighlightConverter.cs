using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ElfLens.Core.ViewModels;

namespace ElfLens.Converters;

public static class HighlightConverters
{
    public static readonly IValueConverter LineTypeBrush = new LineTypeToBrushConverter();

    private class LineTypeToBrushConverter : IValueConverter
    {
        private static SolidColorBrush B(string h) => new(Color.Parse(h));

        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is LineType type)
            {
                return type switch
                {
                    LineType.Function => B("#4FC3F7"),
                    LineType.Branch => B("#FFB74D"),
                    LineType.Call => B("#81C784"),
                    LineType.Ret => B("#EF5350"),
                    LineType.Instruction => B("#B0BEC5"),
                    _ => B("#757575")
                };
            }
            return B("#757575");
        }

        public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}
