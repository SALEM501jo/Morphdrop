using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PdfSharp.Drawing;

namespace Morphdrop.Pdf;

/// <summary>
/// Text for PDF pages, laid out by WPF (so Arabic is joined and runs right to left, and long lines wrap) and written
/// into the PDF as outlines. The PDF needs no embedded font, and the text looks exactly like the preview.
/// </summary>
internal static class PdfText
{
    public static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public static readonly Typeface SemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public static readonly Color Ink = Color.FromRgb(0x1E, 0x1E, 0x24);

    /// <summary>True when the first letter belongs to a right-to-left script (Arabic, Hebrew, Syriac, Thaana…).</summary>
    public static bool IsRightToLeft(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF')
                return true;
            if (char.IsLetter(c))
                return false;
        }

        return false;
    }

    /// <summary>The text wrapped to <paramref name="width"/>, centered unless told otherwise. Sizes are in points.</summary>
    public static FormattedText Format(string text, double width, double size, Typeface typeface, Color? color = null, TextAlignment alignment = TextAlignment.Center) =>
        new(text, CultureInfo.CurrentCulture,
            IsRightToLeft(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            typeface, size, Frozen(color ?? Ink), 1.0)
        {
            MaxTextWidth = Math.Max(1, width),
            TextAlignment = alignment,
        };

    /// <summary>The top-left of the area: WPF lays right-to-left text out inside the same box.</summary>
    public static Point Origin(FormattedText text, Rect area) => area.TopLeft;

    public static void Draw(DrawingContext context, FormattedText text, Rect area) => context.DrawText(text, Origin(text, area));

    /// <summary>Writes the text into a PDF page as filled outlines, in <paramref name="color"/> (ink by default).</summary>
    public static void Draw(XGraphics graphics, FormattedText text, Rect area, Color? color = null)
    {
        Geometry outline = text.BuildGeometry(Origin(text, area));
        if (outline.IsEmpty())
            return;

        PathGeometry path = PathGeometry.CreateFromGeometry(outline);
        var pdfPath = new XGraphicsPath { FillMode = path.FillRule == FillRule.Nonzero ? XFillMode.Winding : XFillMode.Alternate };
        foreach (PathFigure figure in path.Figures)
        {
            pdfPath.StartFigure();
            Point current = figure.StartPoint;
            foreach (PathSegment segment in figure.Segments)
                current = Add(pdfPath, segment, current);
            if (figure.IsClosed)
                pdfPath.CloseFigure();
        }

        Color fill = color ?? Ink;
        graphics.DrawPath(new XSolidBrush(XColor.FromArgb(fill.A, fill.R, fill.G, fill.B)), pdfPath);
    }

    private static Point Add(XGraphicsPath path, PathSegment segment, Point current)
    {
        switch (segment)
        {
            case LineSegment line:
                path.AddLine(X(current), X(line.Point));
                return line.Point;
            case PolyLineSegment lines:
                foreach (Point point in lines.Points)
                {
                    path.AddLine(X(current), X(point));
                    current = point;
                }

                return current;
            case BezierSegment bezier:
                path.AddBezier(X(current), X(bezier.Point1), X(bezier.Point2), X(bezier.Point3));
                return bezier.Point3;
            case PolyBezierSegment beziers:
                for (int i = 0; i + 2 < beziers.Points.Count; i += 3)
                {
                    path.AddBezier(X(current), X(beziers.Points[i]), X(beziers.Points[i + 1]), X(beziers.Points[i + 2]));
                    current = beziers.Points[i + 2];
                }

                return current;
            case QuadraticBezierSegment quadratic:
                return AddQuadratic(path, current, quadratic.Point1, quadratic.Point2);
            case PolyQuadraticBezierSegment quadratics:
                for (int i = 0; i + 1 < quadratics.Points.Count; i += 2)
                    current = AddQuadratic(path, current, quadratics.Points[i], quadratics.Points[i + 1]);
                return current;
            default:
                // Glyph outlines have no arcs; anything else is approximated by flattening it.
                var piece = new PathGeometry(new[] { new PathFigure(current, new[] { segment }, false) });
                foreach (PathFigure flat in piece.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute).Figures)
                {
                    foreach (PathSegment part in flat.Segments)
                        current = Add(path, part, current);
                }

                return current;
        }
    }

    /// <summary>PDF curves are cubic; a quadratic one is the cubic with its control points two thirds of the way.</summary>
    private static Point AddQuadratic(XGraphicsPath path, Point start, Point control, Point end)
    {
        Point first = start + (control - start) * (2.0 / 3);
        Point second = end + (control - end) * (2.0 / 3);
        path.AddBezier(X(start), X(first), X(second), X(end));
        return end;
    }

    private static XPoint X(Point point) => new(point.X, point.Y);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
