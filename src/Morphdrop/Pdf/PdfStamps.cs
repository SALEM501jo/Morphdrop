using System.Windows;
using System.Windows.Media;
using Morphdrop.Actions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Morphdrop.Pdf;

/// <summary>Something put on a page: typed text, or a picture (a signature). Bounds are in points, from the page's top-left.</summary>
internal abstract record Stamp(int Page, Rect Bounds);

internal sealed record TextStamp(int Page, Rect Bounds, string Text, double FontSize) : Stamp(Page, Bounds);

internal sealed record PictureStamp(int Page, Rect Bounds, byte[] Png) : Stamp(Page, Bounds);

/// <summary>Writes stamps onto a copy of a PDF: text as outlines (so any language looks right), pictures as images.</summary>
internal static class PdfStamps
{
    /// <summary>How far WPF's text box sets its text in from the box's edge; the PDF puts it in the same place.</summary>
    public static readonly Vector TextInset = new(2, 0);

    public static string Save(string path, IReadOnlyList<Stamp> stamps)
    {
        if (stamps.Count == 0)
            throw new NothingToDoException("Nothing was added to the pages.");

        using PdfDocument document = PdfTools.Open(path, PdfDocumentOpenMode.Modify);
        foreach (IGrouping<int, Stamp> page in stamps.GroupBy(stamp => stamp.Page))
        {
            using XGraphics graphics = VisiblePage.Open(document.Pages[page.Key], out _);
            foreach (Stamp stamp in page)
            {
                switch (stamp)
                {
                    case TextStamp text when text.Text.Trim().Length > 0:
                        var area = new Rect(text.Bounds.TopLeft + TextInset, new Size(text.Bounds.Width + 4, text.Bounds.Height));
                        PdfText.Draw(graphics, PdfText.Format(text.Text, area.Width, text.FontSize, PdfText.Regular, alignment: TextAlignment.Left), area);
                        break;
                    case PictureStamp picture:
                        using (var stream = new MemoryStream(picture.Png))
                        using (XImage image = XImage.FromStream(stream))
                            graphics.DrawImage(image, picture.Bounds.X, picture.Bounds.Y, picture.Bounds.Width, picture.Bounds.Height);
                        break;
                }
            }
        }

        document.Options.CompressContentStreams = true;
        return OutputFile.Save(path, "signed", ".pdf", output => document.Save(output, closeStream: false));
    }

    /// <summary>The visible size of each page in points (turned pages swap width and height), for laying out the window.</summary>
    public static IReadOnlyList<Size> PageSizes(string path)
    {
        using PdfDocument document = PdfTools.Open(path, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>().Select(page =>
        {
            double width = page.Width.Point, height = page.Height.Point;
            return page.Rotate % 180 == 0 ? new Size(width, height) : new Size(height, width);
        }).ToList();
    }
}
