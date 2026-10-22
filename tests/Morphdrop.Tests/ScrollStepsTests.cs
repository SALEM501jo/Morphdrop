using Morphdrop.Input;

namespace Morphdrop.Tests;

public sealed class ScrollStepsTests
{
    [Fact]
    public void AMouseWheelClick_TurnsOnePage()
    {
        var steps = new ScrollSteps();

        Assert.Equal(-1, steps.Add(-120, 1000));
        Assert.Equal(1, steps.Add(120, 2000));
    }

    [Fact]
    public void ATouchpadsSmallSteps_AddUpToOneTurn()
    {
        var steps = new ScrollSteps();
        int turns = 0;

        for (int i = 0; i < 12; i++)
            turns += Math.Abs(steps.Add(-10, 1000 + i * 8)); // 12 × 10 = one notch

        Assert.Equal(1, turns);
    }

    [Fact]
    public void AFastSwipe_TurnsAtMostFourPagesASecond()
    {
        var steps = new ScrollSteps();
        int turns = 0;

        // A hard swipe with inertia: 100 events of 60, 10 ms apart (a whole second).
        for (int i = 0; i < 100; i++)
            turns += Math.Abs(steps.Add(-60, 1000 + i * 10));

        Assert.InRange(turns, 3, 4);
    }

    [Fact]
    public void ChangingDirection_StartsCountingAgain()
    {
        var steps = new ScrollSteps();

        Assert.Equal(0, steps.Add(-100, 1000));
        Assert.Equal(0, steps.Add(60, 1010)); // not 120 the other way yet
        Assert.Equal(1, steps.Add(60, 1020));
    }
}
