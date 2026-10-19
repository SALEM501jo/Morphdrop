using System.Windows;

namespace ShiftDrop.Editing;

[Flags]
internal enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>The crop box's geometry, in preview pixels: dragging edges, moving, and fixed aspect ratios.</summary>
internal static class CropMath
{
    public const double MinSize = 16;

    /// <summary>The largest box with this width/height ratio, centred in the image.</summary>
    public static Rect Largest(Size bounds, double ratio)
    {
        double width = Math.Min(bounds.Width, bounds.Height * ratio);
        double height = width / ratio;
        return new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
    }

    public static Rect Move(Rect crop, double dx, double dy, Size bounds) => new(
        Math.Clamp(crop.X + dx, 0, bounds.Width - crop.Width),
        Math.Clamp(crop.Y + dy, 0, bounds.Height - crop.Height),
        crop.Width,
        crop.Height);

    /// <summary>
    /// Drags the given edges. With a ratio, the width follows the height for top/bottom handles and the height follows
    /// the width otherwise, keeping the opposite edges in place; a drag that would leave the image is ignored.
    /// </summary>
    public static Rect Resize(Rect crop, Edges edges, double dx, double dy, Size bounds, double? ratio)
    {
        double left = crop.Left, top = crop.Top, right = crop.Right, bottom = crop.Bottom;
        if (edges.HasFlag(Edges.Left))
            left = Math.Clamp(left + dx, 0, right - MinSize);
        if (edges.HasFlag(Edges.Right))
            right = Math.Clamp(right + dx, left + MinSize, bounds.Width);
        if (edges.HasFlag(Edges.Top))
            top = Math.Clamp(top + dy, 0, bottom - MinSize);
        if (edges.HasFlag(Edges.Bottom))
            bottom = Math.Clamp(bottom + dy, top + MinSize, bounds.Height);

        if (ratio is double r)
        {
            bool horizontal = edges.HasFlag(Edges.Left) || edges.HasFlag(Edges.Right);
            double width = horizontal ? right - left : (bottom - top) * r;
            double height = width / r;

            if (edges.HasFlag(Edges.Left))
                left = right - width;
            else
                right = left + width;
            if (edges.HasFlag(Edges.Top))
                top = bottom - height;
            else
                bottom = top + height;

            const double tolerance = 0.5;
            if (left < -tolerance || top < -tolerance || right > bounds.Width + tolerance || bottom > bounds.Height + tolerance
                || width < MinSize || height < MinSize)
            {
                return crop;
            }
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }
}
