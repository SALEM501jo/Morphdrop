using System.Buffers.Binary;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ShiftDrop.Imaging;

/// <summary>
/// Renders PDF pages with the PDF engine built into Windows (Windows.Data.Pdf, the one Edge uses), so no
/// Ghostscript or PDFium is needed. Pages come out as PNG on a white background.
/// </summary>
internal static class PdfPages
{
    /// <summary>Sharp enough to read small print and to print again.</summary>
    public const double RenderDpi = 200;

    /// <summary>Keeps posters and huge drawings from producing gigantic images.</summary>
    public const uint MaxSide = 8000;

    public static bool IsPdf(string path)
    {
        Span<byte> header = stackalloc byte[5];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return stream.ReadAtLeast(header, 5, throwOnEndOfStream: false) == 5 && header.SequenceEqual("%PDF-"u8);
    }

    /// <summary>Renders each page in order, handing over (page index, page count, PNG bytes). Returns the page count.</summary>
    public static int Render(string path, Action<int, int, byte[]> onPage)
    {
        PdfDocument document = Open(path);
        int count = (int)document.PageCount;

        // RenderToStreamAsync silently multiplies the requested size by the display's scale (125% → 1.25x), and
        // that scale can't be read reliably from every process. So it is measured on the first page and divided out.
        double displayScale = 1;
        for (int index = 0; index < count; index++)
        {
            using PdfPage page = document.GetPage((uint)index);

            // Page sizes are in DIPs (1/96 inch).
            double scale = RenderDpi / 96;
            double width = page.Size.Width * scale;
            double height = page.Size.Height * scale;
            double fit = Math.Min(1, MaxSide / Math.Max(width, height));
            uint targetWidth = (uint)Math.Max(1, Math.Round(width * fit));
            uint targetHeight = (uint)Math.Max(1, Math.Round(height * fit));

            byte[] png = RenderPage(page, targetWidth / displayScale, targetHeight / displayScale);
            uint renderedWidth = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)); // IHDR width
            if (index == 0 && Math.Abs(renderedWidth - (double)targetWidth) > 1)
            {
                displayScale = renderedWidth / (double)targetWidth;
                png = RenderPage(page, targetWidth / displayScale, targetHeight / displayScale);
            }

            onPage(index, count, png);
        }

        return count;
    }

    private static byte[] RenderPage(PdfPage page, double width, double height)
    {
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(width)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(height)),
        };

        using var rendered = new InMemoryRandomAccessStream();
        page.RenderToStreamAsync(rendered, options).AsTask().GetAwaiter().GetResult();
        using var png = new MemoryStream();
        using (Stream source = rendered.AsStreamForRead())
            source.CopyTo(png);
        return png.ToArray();
    }

    private static PdfDocument Open(string path)
    {
        try
        {
            StorageFile file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
            return PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
        {
            // Windows reports a password-protected PDF as ERROR_WRONG_PASSWORD (0x8007052B).
            string reason = ex.HResult == unchecked((int)0x8007052B)
                ? "This PDF is password-protected."
                : "Windows couldn't open this PDF; it may be damaged.";
            throw new InvalidDataException(reason, ex);
        }
    }
}
