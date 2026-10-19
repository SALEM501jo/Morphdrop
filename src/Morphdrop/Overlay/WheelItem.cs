namespace Morphdrop.Overlay;

/// <summary>What one slice shows: a label, optionally with an icon glyph above it.</summary>
public sealed record WheelItem(string Label, string? Glyph = null);
