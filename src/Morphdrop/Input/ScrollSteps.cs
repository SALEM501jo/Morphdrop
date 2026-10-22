namespace Morphdrop.Input;

/// <summary>
/// Turns scroll-wheel input into page turns: one page per full notch (120, a mouse wheel's click), and no faster than
/// one every quarter second. Touchpads and smooth-scrolling mice send many small steps and keep scrolling by inertia;
/// without this, one swipe would spin through every page.
/// </summary>
internal sealed class ScrollSteps
{
    public const int Notch = 120;
    public const int MinimumGapMs = 250;

    private int _pending;
    private long _lastTurn = long.MinValue / 2;

    /// <returns>1 (up), −1 (down) when a page should turn, otherwise 0.</returns>
    public int Add(int delta, long nowMs)
    {
        if (delta == 0)
            return 0;
        if (Math.Sign(delta) != Math.Sign(_pending))
            _pending = 0; // changed direction: start counting again

        _pending += delta;
        if (Math.Abs(_pending) < Notch)
            return 0;

        int direction = Math.Sign(_pending);
        _pending = 0;
        if (nowMs - _lastTurn < MinimumGapMs)
            return 0;

        _lastTurn = nowMs;
        return direction;
    }
}
