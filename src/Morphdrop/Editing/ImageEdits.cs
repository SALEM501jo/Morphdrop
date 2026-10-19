using ImageMagick;
using ImageMagick.Drawing;

namespace Morphdrop.Editing;

internal enum RedactStyle { BlackBox, Pixelate }

/// <summary>The pixel work behind the editors, applied to every frame so animations stay animated.</summary>
internal static class ImageEdits
{
    public static void Crop(MagickImageCollection frames, PixelRect area)
    {
        foreach (IMagickImage<byte> frame in frames)
        {
            PixelRect inside = area.ClampTo(frame.Width, frame.Height);
            if (inside.IsEmpty)
                throw new ArgumentException("The crop area is outside the image.", nameof(area));

            frame.Crop(new MagickGeometry(inside.X, inside.Y, (uint)inside.Width, (uint)inside.Height));
            frame.ResetPage(); // forget the old canvas offset, or viewers pad the result back to full size
        }
    }

    /// <summary>
    /// Covers each area with black, or with coarse blocks (at least a tenth of the area, so text can't be read back).
    /// Also removes EXIF, XMP and IPTC: an EXIF thumbnail would still show the unredacted picture.
    /// </summary>
    public static void Redact(MagickImageCollection frames, IReadOnlyList<PixelRect> areas, RedactStyle style)
    {
        foreach (IMagickImage<byte> frame in frames)
        {
            foreach (PixelRect area in areas)
            {
                PixelRect inside = area.ClampTo(frame.Width, frame.Height);
                if (inside.IsEmpty)
                    continue;

                if (style == RedactStyle.BlackBox)
                {
                    new Drawables()
                        .FillColor(MagickColors.Black)
                        .Rectangle(inside.X, inside.Y, inside.X + inside.Width - 1, inside.Y + inside.Height - 1)
                        .Draw(frame);
                }
                else
                {
                    Pixelate(frame, inside);
                }
            }

            foreach (string profile in new[] { "exif", "xmp", "iptc", "8bim" })
                frame.RemoveProfile(profile);
        }
    }

    private static void Pixelate(IMagickImage<byte> frame, PixelRect area)
    {
        int block = Math.Max(8, Math.Max(area.Width, area.Height) / 10);
        using IMagickImage<byte> region = frame.Clone();
        region.Crop(new MagickGeometry(area.X, area.Y, (uint)area.Width, (uint)area.Height));
        region.ResetPage();
        region.Sample((uint)Math.Max(1, (area.Width + block - 1) / block), (uint)Math.Max(1, (area.Height + block - 1) / block));
        region.Sample((uint)area.Width, (uint)area.Height); // nearest neighbour: hard blocks
        frame.Composite(region, area.X, area.Y, CompositeOperator.Copy);
    }
}
