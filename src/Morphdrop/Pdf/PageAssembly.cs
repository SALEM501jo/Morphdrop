using Morphdrop.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Morphdrop.Pdf;

/// <summary>A page in the organizer: a page of a PDF (by index), or a picture (index −1), turned by a multiple of 90°.</summary>
internal sealed record AssembledPage(string Source, int PageIndex, int Rotation)
{
    public bool IsPicture => PageIndex < 0;
}

/// <summary>Writes the organizer's pages, in their new order, into one PDF.</summary>
internal static class PageAssembly
{
    /// <summary>
    /// Saves "name (edited).pdf" when every page comes from one PDF, "first name (merged).pdf" otherwise. Pages are
    /// copied as they are (text stays text); pictures get a page of their own shape.
    /// </summary>
    public static string Save(IReadOnlyList<AssembledPage> pages)
    {
        if (pages.Count == 0)
            throw new NothingToDoException("There are no pages left.");

        bool oneSource = pages.Select(page => page.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && !pages[0].IsPicture;
        using PdfDocument document = Create(pages);
        return OutputFile.Save(pages[0].Source, oneSource ? "edited" : "merged", ".pdf", output => document.Save(output, closeStream: false));
    }

    public static PdfDocument Create(IReadOnlyList<AssembledPage> pages)
    {
        var sources = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
        var document = new PdfDocument();
        document.Info.Creator = "Morphdrop";
        document.Options.CompressContentStreams = true;
        try
        {
            foreach (AssembledPage page in pages)
            {
                PdfPage added;
                if (page.IsPicture)
                {
                    added = AddPicture(document, page.Source);
                }
                else
                {
                    if (!sources.TryGetValue(page.Source, out PdfDocument? source))
                        sources[page.Source] = source = PdfTools.Open(page.Source, PdfDocumentOpenMode.Import);
                    added = document.AddPage(source.Pages[page.PageIndex]);
                }

                added.Rotate = ((added.Rotate + page.Rotation) % 360 + 360) % 360;
            }
        }
        finally
        {
            foreach (PdfDocument source in sources.Values)
                source.Dispose();
        }

        return document;
    }

    /// <summary>A page exactly the shape of the picture, as wide as A4 (or as tall, for a landscape picture).</summary>
    private static PdfPage AddPicture(PdfDocument document, string path)
    {
        PdfPicture picture = PdfPictures.Prepare(path);
        var options = PageOptions.Default with { Paper = PaperSize.Picture, Margin = PageMargin.None };
        PageLayout layout = PictureDocument.Layout(new PicturePage(path), picture, options);
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromPoint(layout.Page.Width);
        page.Height = XUnit.FromPoint(layout.Page.Height);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        using var stream = new MemoryStream(picture.Data);
        using XImage image = XImage.FromStream(stream);
        graphics.DrawImage(image, layout.Picture.X, layout.Picture.Y, layout.Picture.Width, layout.Picture.Height);
        return page;
    }
}
