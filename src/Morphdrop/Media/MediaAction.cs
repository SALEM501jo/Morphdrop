using Morphdrop.Actions;

namespace Morphdrop.Media;

/// <summary>A slice that needs FFmpeg: the wheel makes sure it's installed before the job starts.</summary>
internal sealed class MediaAction : IFileAction
{
    private readonly Func<string, bool> _appliesTo;
    private readonly Func<string, string, string> _run;

    public MediaAction(string label, string? glyph, string description, Func<string, bool> appliesTo, Func<string, string, string> run)
    {
        Label = label;
        Glyph = glyph;
        Description = description;
        _appliesTo = appliesTo;
        _run = run;
    }

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => _appliesTo(path);

    public string Run(string inputPath) => _run(inputPath, Description);

    public static IEnumerable<IFileAction> ConvertActions()
    {
        // From any video (and from GIFs, which make far smaller MP4s).
        yield return Convert("MP4", "Convert to MP4", MediaTarget.Mp4,
            path => (MediaConverter.IsVideo(path) || IsGif(path)) && !HasExtension(path, ".mp4"));
        yield return Convert("GIF", "Make a GIF (640 px, 15 fps)", MediaTarget.Gif, MediaConverter.IsVideo);
        yield return Convert("WEBP", "Make an animated WebP", MediaTarget.WebP, MediaConverter.IsVideo);

        // Sound, from video or audio.
        yield return Convert("MP3", "Save the sound as MP3", MediaTarget.Mp3, path => HasSound(path) && !HasExtension(path, ".mp3"));
        yield return Convert("M4A", "Save the sound as M4A (AAC)", MediaTarget.M4a, path => HasSound(path) && !HasExtension(path, ".m4a"));
        yield return Convert("WAV", "Convert to WAV", MediaTarget.Wav, path => MediaConverter.IsAudio(path) && !HasExtension(path, ".wav"));
        yield return Convert("FLAC", "Convert to FLAC (lossless)", MediaTarget.Flac, path => MediaConverter.IsAudio(path) && !HasExtension(path, ".flac"));
    }

    public static IEnumerable<IFileAction> ToolActions(string compressGlyph)
    {
        yield return new MediaAction("Compress", compressGlyph, "Make the video smaller", MediaConverter.IsVideo, MediaConverter.Compress);
        yield return new MediaAction("720p", "", "Resize to 720p", MediaConverter.IsVideo, MediaConverter.To720p);
        yield return new MediaAction("Mute", "", "Remove the sound", MediaConverter.IsVideo, MediaConverter.Mute);
    }

    private static MediaAction Convert(string label, string description, MediaTarget target, Func<string, bool> appliesTo) =>
        new(label, null, description, appliesTo, (path, text) => MediaConverter.Convert(path, target, text));

    private static bool HasSound(string path) => MediaConverter.IsVideo(path) || MediaConverter.IsAudio(path);

    private static bool IsGif(string path) => HasExtension(path, ".gif");

    private static bool HasExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
}
