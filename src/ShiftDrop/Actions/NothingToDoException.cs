namespace ShiftDrop.Actions;

/// <summary>An action found nothing to change (e.g. the image is already small enough). Reported, not an error.</summary>
internal sealed class NothingToDoException : Exception
{
    public NothingToDoException(string message)
        : base(message)
    {
    }
}
