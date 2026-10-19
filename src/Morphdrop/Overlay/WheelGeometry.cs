using System.Windows;
using System.Windows.Media;

namespace Morphdrop.Overlay;

/// <summary>
/// Layout of the wheel in device-independent pixels. Slices go clockwise, with slice 0 centred at the top.
/// Angles are in degrees, 0° pointing right and growing clockwise (screen y points down).
/// </summary>
internal static class WheelGeometry
{
    public const double Size = 360;
    public const double RingRadius = 168;
    public const double OuterRadius = 160;
    public const double InnerRadius = 66;
    public const double LabelRadius = (InnerRadius + OuterRadius) / 2;
    public const double SliceGap = 6;
    public const double CornerRadius = 7;

    public const int HubIndex = -1;
    public const int OutsideIndex = -2;

    public static Point Center { get; } = new(Size / 2, Size / 2);

    public static double SliceCenterAngle(int index, int count) => -90 + index * 360.0 / count;

    public static Point PointAt(double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180;
        return new Point(Center.X + radius * Math.Cos(radians), Center.Y + radius * Math.Sin(radians));
    }

    /// <summary>The slice under <paramref name="point"/>, <see cref="HubIndex"/> or <see cref="OutsideIndex"/>.</summary>
    public static int HitTest(Point point, int sliceCount)
    {
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;
        double radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < InnerRadius)
            return HubIndex;
        if (radius > RingRadius || sliceCount == 0)
            return OutsideIndex;

        double sweep = 360.0 / sliceCount;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        double fromFirstEdge = ((angle + 90 + sweep / 2) % 360 + 360) % 360;
        return Math.Min((int)(fromFirstEdge / sweep), sliceCount - 1);
    }

    /// <summary>
    /// The shape of one slice, shrunk by <see cref="CornerRadius"/> on every side: drawing it with a round-joined
    /// stroke 2 × CornerRadius wide grows it back to full size with rounded corners and an even gap between slices.
    /// </summary>
    public static Geometry CreateSlice(int index, int count)
    {
        double inner = InnerRadius + CornerRadius;
        double outer = OuterRadius - CornerRadius;
        Geometry geometry;

        if (count == 1)
        {
            geometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new EllipseGeometry(Center, outer, outer),
                new EllipseGeometry(Center, inner, inner));
        }
        else
        {
            double sweep = 360.0 / count;
            double start = SliceCenterAngle(index, count) - sweep / 2;
            double end = start + sweep;

            // Side edges run parallel to the slice boundary at this distance, so the gap has the same width everywhere.
            double offset = SliceGap / 2 + CornerRadius;
            double innerInset = Math.Asin(offset / inner) * 180 / Math.PI;
            double outerInset = Math.Asin(offset / outer) * 180 / Math.PI;

            var figure = new PathFigure { StartPoint = PointAt(outer, start + outerInset), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new ArcSegment(
                PointAt(outer, end - outerInset), new Size(outer, outer), 0,
                sweep - 2 * outerInset > 180, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(PointAt(inner, end - innerInset), true));
            figure.Segments.Add(new ArcSegment(
                PointAt(inner, start + innerInset), new Size(inner, inner), 0,
                sweep - 2 * innerInset > 180, SweepDirection.Counterclockwise, true));
            geometry = new PathGeometry(new[] { figure });
        }

        geometry.Freeze();
        return geometry;
    }
}
