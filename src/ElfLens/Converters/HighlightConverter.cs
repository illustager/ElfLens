using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;

namespace ElfLens.Converters;

public static class HighlightConverters
{
    public static readonly IValueConverter HexToBrush = new HexToBrushConverter();
    public static readonly IValueConverter CurrentBg = new CurrentBgConverter();
    public static readonly IValueConverter BreakpointBorder = new BreakpointBorderConverter();
    public static readonly IValueConverter NavCursor = new NavCursorConverter();

    private class HexToBrushConverter : IValueConverter
    {
        private static readonly Dictionary<string, SolidColorBrush> Cache = new();
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is not string hex || hex.Length == 0) hex = "#B0BEC5";
            if (Cache.TryGetValue(hex, out var b)) return b;
            try { b = new SolidColorBrush(Color.Parse(hex)); }
            catch { b = new SolidColorBrush(Colors.White); }
            Cache[hex] = b;
            return b;
        }
        public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private class BreakpointBorderConverter : IValueConverter
    {
        private static readonly SolidColorBrush BpEnabled = new(Color.Parse("#F44336"));  // red
        private static readonly SolidColorBrush BpDisabled = new(Color.Parse("#FF9800")); // orange
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is Core.ViewModels.HighlightedLine line)
            {
                if (line.IsBreakpointDisabled) return BpDisabled;
                if (line.IsBreakpoint) return BpEnabled;
            }
            return Brushes.Transparent;
        }
        public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private class CurrentBgConverter : IValueConverter
    {
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is bool isCur && isCur)
                return new SolidColorBrush(Color.Parse("#3A3A00"));
            return Brushes.Transparent;
        }
        public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private class NavCursorConverter : IValueConverter
    {
        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is string nav && nav.Length > 0) return HandCursor;
            return Cursor.Default;
        }
        public object? ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}
