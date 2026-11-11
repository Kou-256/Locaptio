using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Locaptio.WinUI;

internal static class CaptionTail
{
    // Fit the most recent complete Unicode text elements, with no ellipsis and
    // no partially scrolled first line. The transcript retains the original text.
    internal static string Fit(TextBlock style, string text, double width, int maxLines)
    {
        if (text.Length == 0) return text;
        var probe = new TextBlock
        {
            FontFamily = style.FontFamily, FontSize = style.FontSize, FontWeight = style.FontWeight,
            FontStyle = style.FontStyle, CharacterSpacing = style.CharacterSpacing,
            LineHeight = style.LineHeight, LineStackingStrategy = style.LineStackingStrategy,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, Text = "国"
        };
        var size = new Size(Math.Max(1, width), double.PositiveInfinity);
        probe.Measure(size); double lineHeight = probe.DesiredSize.Height;
        probe.Text = "M"; probe.Measure(size); lineHeight = Math.Min(lineHeight, probe.DesiredSize.Height);
        double budget = lineHeight * Math.Max(1, maxLines) + .1;
        bool Fits(string value) { probe.Text = value; probe.Measure(size); return probe.DesiredSize.Height <= budget; }
        if (Fits(text)) return text;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Fits(text[starts[mid]..])) hi = mid; else lo = mid + 1;
        }
        return text[starts[lo]..];
    }
}
