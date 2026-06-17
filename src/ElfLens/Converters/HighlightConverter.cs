using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;

namespace ElfLens.Converters;

public static class HighlightConverters
{
    public static readonly IValueConverter HexToBrush = new HexToBrushConverter();
    public static readonly IValueConverter CurrentBg = new CurrentBgConverter();
    public static readonly IValueConverter BreakpointBorder = new BreakpointBorderConverter();
    public static readonly IValueConverter FuncHighlightBg = new FuncHighlightBgConverter();
    public static readonly IValueConverter NavCursor = new NavCursorConverter();
    public static readonly IMultiValueConverter PcHighlight = new PcHighlightConverter();

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
        private static readonly SolidColorBrush BpBrush = new(Color.Parse("#F44336"));
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is bool isBp && isBp) return BpBrush;
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

    private class FuncHighlightBgConverter : IValueConverter
    {
        private static readonly SolidColorBrush HighlightBrush = new(Color.Parse("#1A2A3A"));
        private static readonly SolidColorBrush NormalBrush = new(Color.Parse("#1E1E1E"));

        public object? Convert(object? value, Type t, object? p, CultureInfo c)
        {
            if (value is bool isCur && isCur) return HighlightBrush;
            return NormalBrush;
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

    private class PcHighlightConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush Highlight = new(Color.Parse("#3A3A00"));
        public object? Convert(IList<object?> values, Type t, object? p, CultureInfo c)
        {
            if (values.Count >= 2 &&
                values[0] is Core.ViewModels.HighlightedLine line &&
                values[1] is string pc && pc.Length > 0)
            {
                var first = line.Tokens.FirstOrDefault();
                if (first?.Text.TrimStart().Contains(pc.TrimStart('0'), StringComparison.OrdinalIgnoreCase) == true)
                    return Highlight;
            }
            return Brushes.Transparent;
        }
    }
}
