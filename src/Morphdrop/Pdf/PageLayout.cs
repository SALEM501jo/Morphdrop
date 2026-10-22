using System.Windows;

namespace Morphdrop.Pdf;

internal enum PaperSize { A4, Letter, Picture }

internal enum PageOrientation { Auto, Portrait, Landscape }

internal enum PageMargin { None, Small, Normal }

internal enum TextSize { Small, Medium, Large }

/// <summary>How the pages of a picture PDF look. Every page uses the same settings.</summary>
internal sealed record PageOptions(PaperSize Paper, PageOrientation Orientation, PageMargin Margin, TextSize TextSize, bool PageNumbers)
{
    public static PageOptions Default { get; } = new(PaperSize.A4, PageOrientation.Auto, PageMargin.Normal, TextSize.Medium, PageNumbers: false);

    public double MarginPoints => Margin switch { PageMargin.None => 0, PageMargin.Small => 18, _ => 42 };

    public double FontSize => TextSize switch { TextSize.Small => 11, TextSize.Large => 20, _ => 14 };
}

/// <summary>
/// Where things go on one page, in points (1/72 inch): the text above, the picture and the text below sit together
/// as one block in the middle of the page, like a picture with captions in Word; page numbers sit at the bottom.
/// </summary>
internal sealed record PageLayout(Size Page, Rect Above, Rect Picture, Rect Below, Rect Number)
{
    public const double Gap = 12;
    public const double NumberSize = 9;
    private const double NumberBand = 22;
    private const double TextInset = 24; // text keeps off the paper's edge even without margins
    private static readonly Size A4 = new(595.28, 841.89);
    private static readonly Size Letter = new(612, 792);

    /// <param name="picture">The picture's size in pixels (only its shape matters).</param>
    /// <param name="aboveHeight">How tall the text above is when wrapped to a width (0 when there is none).</param>
    /// <param name="belowHeight">The same for the text below.</param>
    public static PageLayout Compute(Size picture, PageOptions options, Func<double, double> aboveHeight, Func<double, double> belowHeight)
    {
        bool landscape = options.Orientation switch
        {
            PageOrientation.Portrait => false,
            PageOrientation.Landscape => true,
            _ => picture.Width > picture.Height,
        };
        double margin = options.MarginPoints;
        double numbers = options.PageNumbers ? Math.Max(0, NumberBand - margin * 0.5) : 0;

        Size paper = options.Paper switch
        {
            PaperSize.Letter => Letter,
            _ => A4,
        };
        if (landscape)
            paper = new Size(paper.Height, paper.Width);

        double textWidth = paper.Width - 2 * Math.Max(margin, TextInset);
        double above = aboveHeight(textWidth);
        double below = belowHeight(textWidth);
        double aboveBlock = above > 0 ? above + Gap : 0;
        double belowBlock = below > 0 ? below + Gap : 0;

        Size page;
        Rect pictureArea;
        if (options.Paper == PaperSize.Picture)
        {
            // As wide as A4 (or A4 on its side), as tall as the picture and its text need.
            double width = paper.Width - 2 * margin;
            double height = width * picture.Height / Math.Max(1, picture.Width);
            page = new Size(paper.Width, height + aboveBlock + belowBlock + numbers + 2 * margin);
            pictureArea = new Rect(margin, margin + aboveBlock, width, height);
        }
        else
        {
            page = paper;
            double available = page.Height - 2 * margin - aboveBlock - belowBlock - numbers;
            var area = new Size(page.Width - 2 * margin, Math.Max(1, available));
            double scale = Math.Min(area.Width / Math.Max(1, picture.Width), area.Height / Math.Max(1, picture.Height));
            var fitted = new Size(picture.Width * scale, picture.Height * scale);
            double blockHeight = aboveBlock + fitted.Height + belowBlock;
            double top = margin + (page.Height - 2 * margin - numbers - blockHeight) / 2;
            pictureArea = new Rect((page.Width - fitted.Width) / 2, top + aboveBlock, fitted.Width, fitted.Height);
        }

        double textLeft = (page.Width - textWidth) / 2;
        var aboveRect = new Rect(textLeft, pictureArea.Top - aboveBlock, textWidth, above);
        var belowRect = new Rect(textLeft, pictureArea.Bottom + (below > 0 ? Gap : 0), textWidth, below);
        var numberRect = new Rect(0, page.Height - Math.Max(margin * 0.5, 6) - NumberSize - 4, page.Width, NumberSize + 4);
        return new PageLayout(page, aboveRect, pictureArea, belowRect, numberRect);
    }
}
