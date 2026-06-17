using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ElfLens.Core.ViewModels;

namespace ElfLens.Converters;

public static class ShellOutputTypeConverters
{
    public static readonly IValueConverter ToCommandClass = new ShellOutputTypeToClassConverter();

    private class ShellOutputTypeToClassConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ShellOutputType type)
            {
                return type switch
                {
                    ShellOutputType.Command => "Command",
                    ShellOutputType.Error => "Error",
                    ShellOutputType.System => "System",
                    _ => "Output"
                };
            }
            return "Output";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
