namespace ShiftDrop.Editing;

/// <summary>A rectangle in image pixels.</summary>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>The part inside a width × height image (possibly empty).</summary>
    public PixelRect ClampTo(uint width, uint height)
    {
        int left = Math.Clamp(X, 0, (int)width);
        int top = Math.Clamp(Y, 0, (int)height);
        int right = Math.Clamp(X + Width, left, (int)width);
        int bottom = Math.Clamp(Y + Height, top, (int)height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Maps a rectangle measured on the preview back to the full-size image.</summary>
    public static PixelRect FromPreview(System.Windows.Rect rect, double scale) => new(
        (int)Math.Round(rect.X * scale),
        (int)Math.Round(rect.Y * scale),
        (int)Math.Round(rect.Width * scale),
        (int)Math.Round(rect.Height * scale));
}
