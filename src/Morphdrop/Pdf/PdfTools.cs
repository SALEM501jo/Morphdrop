using System.Windows;
using ImageMagick;
using Morphdrop.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Morphdrop.Pdf;

/// <summary>The PDF tools that need no window: split into pages, number the pages, make it smaller.</summary>
internal static class PdfTools
{
    private const uint MaxPictureSide = 2000; // about 200 DPI across an A4 page
    private const uint CompressedQuality = 70;

    /// <summary>Opens a PDF with PDFsharp, with a clear message for the ones it can't change.</summary>
    public static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("This PDF is password-protected.", ex);
        }
    }

    /// <summary>Saves every page as its own PDF in a folder "name (pages)": "page 01.pdf", "page 02.pdf"…</summary>
    public static string Split(string path)
    {
        using PdfDocument source = Open(path, PdfDocumentOpenMode.Import);
        if (source.PageCount < 2)
            throw new NothingToDoException("It has only one page.");

        string folder = OutputFile.CreateUniqueFolder(path, "pages");
        int digits = Math.Max(2, source.PageCount.ToString().Length);
        for (int i = 0; i < source.PageCount; i++)
        {
            using var single = new PdfDocument();
            single.Info.Creator = "Morphdrop";
            single.AddPage(source.Pages[i]);
            using FileStream output = OutputFile.CreateUnique(folder, $"page {(i + 1).ToString().PadLeft(digits, '0')}", ".pdf", out _);
            single.Save(output, closeStream: false);
        }

        return folder;
    }

    /// <summary>Adds "1 / 12" at the bottom of every page and saves "name (numbered).pdf".</summary>
    public static string AddPageNumbers(string path)
    {
        using PdfDocument document = Open(path, PdfDocumentOpenMode.Modify);
        int count = document.PageCount;
        for (int i = 0; i < count; i++)
        {
            PdfPage page = document.Pages[i];
            using XGraphics graphics = VisiblePage.Open(page, out System.Windows.Size visible);
            double width = visible.Width, height = visible.Height;
            var area = new Rect(0, height - 26, width, 14);
            PdfText.Draw(graphics, PdfText.Format($"{i + 1} / {count}", width, PageLayout.NumberSize, PdfText.Regular, PictureDocument.NumberColor), area, PictureDocument.NumberColor);
        }

        return Save(document, path, "numbered");
    }

    /// <summary>
    /// Makes a PDF smaller by storing its photos at a sensible size and JPEG quality; text and drawings stay sharp,
    /// since they aren't pictures. Saves "name (compressed).pdf", or says there's nothing to gain.
    /// </summary>
    public static string Compress(string path)
    {
        long before = new FileInfo(path).Length;
        using PdfDocument document = Open(path, PdfDocumentOpenMode.Modify);
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfPage page in document.Pages)
            ShrinkPictures(page.Elements.GetDictionary("/Resources"), seen);

        document.Options.CompressContentStreams = true;
        document.Options.NoCompression = false;
        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);
        if (buffer.Length > before * 0.95)
            throw new NothingToDoException("It's already about as small as it gets.");

        return OutputFile.Save(path, "compressed", ".pdf", output => buffer.WriteTo(output));
    }

    private static void ShrinkPictures(PdfDictionary? resources, HashSet<PdfDictionary> seen)
    {
        PdfDictionary? objects = resources?.Elements.GetDictionary("/XObject");
        if (objects is null)
            return;

        foreach (PdfItem? item in objects.Elements.Values)
        {
            if ((item as PdfReference)?.Value is not PdfDictionary xobject || !seen.Add(xobject))
                continue;

            string subtype = xobject.Elements.GetName("/Subtype");
            if (subtype == "/Form")
                ShrinkPictures(xobject.Elements.GetDictionary("/Resources"), seen); // pictures inside drawings
            else if (subtype == "/Image")
                ShrinkJpeg(xobject);
        }
    }

    /// <summary>Re-encodes a JPEG picture (smaller, and no bigger than needed) when that saves at least 10 %.</summary>
    private static void ShrinkJpeg(PdfDictionary picture)
    {
        PdfItem? filter = picture.Elements["/Filter"];
        bool jpeg = filter is PdfName { Value: "/DCTDecode" } || (filter is PdfArray { Elements.Count: 1 } array && array.Elements[0] is PdfName { Value: "/DCTDecode" });
        string colors = picture.Elements.GetName("/ColorSpace");
        if (!jpeg || picture.Stream is null || colors is not ("/DeviceRGB" or "/DeviceGray") || picture.Elements.ContainsKey("/Decode"))
            return;

        // A soft mask must keep matching the picture's size, so such pictures are only re-encoded, not shrunk.
        bool masked = picture.Elements.ContainsKey("/SMask") || picture.Elements.ContainsKey("/Mask");
        byte[]? original = picture.Stream.Value;
        if (original is null)
            return;

        try
        {
            using var image = new MagickImage(original, new MagickReadSettings { Format = MagickFormat.Jpeg });
            if (!masked && Math.Max(image.Width, image.Height) > MaxPictureSide)
                image.Resize(new MagickGeometry(MaxPictureSide, MaxPictureSide));
            image.Strip();
            image.Quality = CompressedQuality;
            byte[] smaller = image.ToByteArray(MagickFormat.Jpeg);
            if (smaller.Length > original.Length * 0.9)
                return;

            picture.Stream.Value = smaller;
            picture.Elements.SetInteger("/Width", (int)image.Width);
            picture.Elements.SetInteger("/Height", (int)image.Height);
            picture.Elements.SetInteger("/BitsPerComponent", 8);
            picture.Elements.Remove("/DecodeParms");
            picture.Elements.SetName("/ColorSpace", image.ChannelCount >= 3 ? "/DeviceRGB" : "/DeviceGray");
        }
        catch (MagickException)
        {
            // A picture ImageMagick can't read stays as it was.
        }
    }

    private static string Save(PdfDocument document, string path, string suffix)
    {
        document.Options.CompressContentStreams = true;
        return OutputFile.Save(path, suffix, ".pdf", output => document.Save(output, closeStream: false));
    }
}
