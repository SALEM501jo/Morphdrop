using System.Windows;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Morphdrop.Pdf;

/// <summary>
/// Drawing on a page as it is seen. A page can be stored one way and shown turned (its /Rotate), and PDFsharp draws
/// in the stored orientation; this turns the drawing so (0, 0) is the top-left corner the reader sees.
/// </summary>
internal static class VisiblePage
{
    public static XGraphics Open(PdfPage page, out Size visible)
    {
        XGraphics graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        double width = graphics.PageSize.Width, height = graphics.PageSize.Height;
        switch ((page.Rotate % 360 + 360) % 360)
        {
            case 90: // shown turned clockwise
                graphics.MultiplyTransform(new XMatrix(0, -1, 1, 0, 0, height));
                visible = new Size(height, width);
                break;
            case 180:
                graphics.MultiplyTransform(new XMatrix(-1, 0, 0, -1, width, height));
                visible = new Size(width, height);
                break;
            case 270:
                graphics.MultiplyTransform(new XMatrix(0, 1, -1, 0, width, 0));
                visible = new Size(height, width);
                break;
            default:
                visible = new Size(width, height);
                break;
        }

        return graphics;
    }
}
