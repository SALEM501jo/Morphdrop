using System.Windows;
using System.Windows.Media;
using Morphdrop.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Morphdrop.Pdf;

/// <summary>One page of a picture PDF: the picture, with optional text above and below it.</summary>
internal sealed record PicturePage(string Path, string Above = "", string Below = "");

/// <summary>Writes pictures into a PDF, one per page, with their text and optional page numbers.</summary>
internal static class PictureDocument
{
    public static readonly Color NumberColor = Color.FromRgb(0x80, 0x80, 0x88);

    /// <summary>Saves "first name (N pages).pdf" ("name.pdf" for one picture) and returns its path.</summary>
    public static string Save(IReadOnlyList<PicturePage> pages, PageOptions options)
    {
        if (pages.Count == 0)
            throw new NothingToDoException("There are no pages.");

        using var document = Create(pages, options);
        return OutputFile.Save(pages[0].Path, pages.Count > 1 ? $"{pages.Count} pages" : null, ".pdf", output => document.Save(output, closeStream: false));
    }

    public static PdfDocument Create(IReadOnlyList<PicturePage> pages, PageOptions options)
    {
        var document = new PdfDocument();
        document.Info.Creator = "Morphdrop";
        document.Options.CompressContentStreams = true;
        for (int i = 0; i < pages.Count; i++)
        {
            PicturePage page = pages[i];
            PdfPicture picture = PdfPictures.Prepare(page.Path);
            PageLayout layout = Layout(page, picture, options);

            PdfPage pdfPage = document.AddPage();
            pdfPage.Width = XUnit.FromPoint(layout.Page.Width);
            pdfPage.Height = XUnit.FromPoint(layout.Page.Height);
            using XGraphics graphics = XGraphics.FromPdfPage(pdfPage);
            using (var stream = new MemoryStream(picture.Data))
            using (XImage image = XImage.FromStream(stream))
                graphics.DrawImage(image, layout.Picture.X, layout.Picture.Y, layout.Picture.Width, layout.Picture.Height);

            Typeface heading = PdfText.SemiBold;
            if (page.Above.Length > 0)
                PdfText.Draw(graphics, PdfText.Format(page.Above, layout.Above.Width, options.FontSize, heading), layout.Above);
            if (page.Below.Length > 0)
                PdfText.Draw(graphics, PdfText.Format(page.Below, layout.Below.Width, options.FontSize, PdfText.Regular), layout.Below);
            if (options.PageNumbers)
                PdfText.Draw(graphics, PdfText.Format($"{i + 1} / {pages.Count}", layout.Number.Width, PageLayout.NumberSize, PdfText.Regular, NumberColor), layout.Number, NumberColor);
        }

        return document;
    }

    /// <summary>The layout for a page; the preview uses the same one, so it matches the PDF.</summary>
    public static PageLayout Layout(PicturePage page, PdfPicture picture, PageOptions options) =>
        Layout(page, new Size(picture.Width, picture.Height), options);

    public static PageLayout Layout(PicturePage page, Size picture, PageOptions options) =>
        PageLayout.Compute(picture, options,
            width => page.Above.Length > 0 ? PdfText.Format(page.Above, width, options.FontSize, PdfText.SemiBold).Height : 0,
            width => page.Below.Length > 0 ? PdfText.Format(page.Below, width, options.FontSize, PdfText.Regular).Height : 0);
}
