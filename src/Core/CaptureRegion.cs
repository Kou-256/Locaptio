namespace Locaptio.Core;

/// <summary>
/// Part of a display to record, in that display's pixels (top-left origin). The video frame has the region's size,
/// so sizes are kept even for the H.264 encoder and at least <see cref="MinSize"/> pixels.
/// </summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public const int MinSize = 64;

    public override string ToString() => $"X {X}, Y {Y}, {Width}×{Height}";

    /// <summary>
    /// The region moved and shrunk to fit a display of the given size, or null when it covers the whole display
    /// (record the display itself) or the display size is unknown.
    /// </summary>
    public static CaptureRegion? Fit(CaptureRegion region, int displayWidth, int displayHeight)
    {
        if (displayWidth < MinSize || displayHeight < MinSize) return null;
        int width = Math.Clamp(region.Width, MinSize, displayWidth) & ~1, height = Math.Clamp(region.Height, MinSize, displayHeight) & ~1;
        int x = Math.Clamp(region.X, 0, displayWidth - width), y = Math.Clamp(region.Y, 0, displayHeight - height);
        if (x == 0 && y == 0 && width >= (displayWidth & ~1) && height >= (displayHeight & ~1)) return null;
        return new(x, y, width, height);
    }

    /// <summary>The region between two dragged corners, in any order.</summary>
    public static CaptureRegion FromCorners(double x1, double y1, double x2, double y2) =>
        new((int)Math.Round(Math.Min(x1, x2)), (int)Math.Round(Math.Min(y1, y2)), (int)Math.Round(Math.Abs(x2 - x1)), (int)Math.Round(Math.Abs(y2 - y1)));

    /// <summary>"x,y,width,height" for the settings file.</summary>
    public string Serialize() => $"{X},{Y},{Width},{Height}";

    public static CaptureRegion? Parse(string? text)
    {
        var parts = (text ?? "").Split(',');
        return parts.Length == 4 && parts.All(p => int.TryParse(p, out _)) ? new(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])) : null;
    }
}
