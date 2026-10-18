using System.Windows;
using ShiftDrop.Overlay;

namespace ShiftDrop.Tests;

public class WheelGeometryTests
{
    private static Point At(double radius, double angle) => WheelGeometry.PointAt(radius, angle);

    [Fact]
    public void TheCenter_IsTheHub()
    {
        Assert.Equal(WheelGeometry.HubIndex, WheelGeometry.HitTest(WheelGeometry.Center, 5));
    }

    [Fact]
    public void BeyondTheRing_IsOutside()
    {
        Assert.Equal(WheelGeometry.OutsideIndex, WheelGeometry.HitTest(At(WheelGeometry.RingRadius + 1, 30), 5));
    }

    [Fact]
    public void WithoutSlices_TheRingIsOutside()
    {
        Assert.Equal(WheelGeometry.OutsideIndex, WheelGeometry.HitTest(At(WheelGeometry.LabelRadius, 30), 0));
    }

    [Theory]
    [InlineData(-90, 0)] // top
    [InlineData(-50, 0)] // the top slice spans -135° to -45°
    [InlineData(-40, 1)]
    [InlineData(0, 1)] // right
    [InlineData(90, 2)] // bottom
    [InlineData(180, 3)] // left
    [InlineData(-140, 3)]
    public void FourSlices_GoClockwiseFromTheTop(double angle, int expected)
    {
        Assert.Equal(expected, WheelGeometry.HitTest(At(WheelGeometry.LabelRadius, angle), 4));
    }

    [Fact]
    public void EverySliceShape_ContainsItsLabel()
    {
        for (int count = 1; count <= 8; count++)
        {
            for (int index = 0; index < count; index++)
            {
                Point label = At(WheelGeometry.LabelRadius, WheelGeometry.SliceCenterAngle(index, count));
                Assert.True(WheelGeometry.CreateSlice(index, count).FillContains(label), $"slice {index} of {count}");
                Assert.Equal(index, WheelGeometry.HitTest(label, count));
            }
        }
    }
}
