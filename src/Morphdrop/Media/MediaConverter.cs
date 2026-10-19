using Morphdrop.Actions;

namespace Morphdrop.Media;

internal enum MediaTarget { Mp4, Gif, WebP, Mp3, M4a, Wav, Flac }

/// <summary>Video and audio work, done by FFmpeg. Results are saved next to the original, never over it.</summary>
internal static class MediaConverter
{
    private const double MinimumSavings = 0.03;

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".3gp", ".mpg", ".mpeg", ".ts", ".mts",
    };

    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".aiff", ".aif",
    };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    public static string Extension(MediaTarget target) => target switch
    {
        MediaTarget.Mp4 => ".mp4",
        MediaTarget.Gif => ".gif",
        MediaTarget.WebP => ".webp",
        MediaTarget.Mp3 => ".mp3",
        MediaTarget.M4a => ".m4a",
        MediaTarget.Wav => ".wav",
        _ => ".flac",
    };

    public static string Convert(string input, MediaTarget target, string description)
    {
        MediaInfo info = FfmpegRunner.Probe(input);
        if (target is MediaTarget.Mp4 or MediaTarget.Gif or MediaTarget.WebP && !info.HasVideo)
            throw new NothingToDoException("There's no video in this file.");
        if (target is MediaTarget.Mp3 or MediaTarget.M4a or MediaTarget.Wav or MediaTarget.Flac && !info.HasAudio)
            throw new NothingToDoException("There's no sound in this file.");

        string[] encode = target switch
        {
            MediaTarget.Mp4 => H264(crf: 23, scale: "scale=trunc(iw/2)*2:trunc(ih/2)*2", audioBitrate: "160k"),
            // Small and smooth: a palette made for this clip, 15 frames a second, at most 640 px wide.
            MediaTarget.Gif => new[]
            {
                "-vf", "fps=15,scale='min(640,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4",
                "-loop", "0",
            },
            MediaTarget.WebP => new[]
            {
                "-vf", "fps=20,scale='min(720,iw)':-1:flags=lanczos",
                "-c:v", "libwebp_anim", "-lossless", "0", "-q:v", "70", "-loop", "0", "-an",
            },
            MediaTarget.Mp3 => new[] { "-vn", "-c:a", "libmp3lame", "-q:a", "2" }, // VBR, about 190 kbit/s
            MediaTarget.M4a => new[] { "-vn", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart" },
            MediaTarget.Wav => new[] { "-vn", "-c:a", "pcm_s16le" },
            _ => new[] { "-vn", "-c:a", "flac" },
        };

        return Encode(input, suffix: null, Extension(target), encode, info, description);
    }

    /// <summary>H.264 at a lower quality setting, fitted within 1920 px; kept only when clearly smaller.</summary>
    public static string Compress(string input, string description)
    {
        MediaInfo info = FfmpegRunner.Probe(input);
        if (!info.HasVideo)
            throw new NothingToDoException("There's no video in this file.");

        string output = Encode(input, "compressed", ".mp4",
            H264(crf: 28, scale: "scale=w='min(1920,iw)':h='min(1920,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2", audioBitrate: "128k"),
            info, description);
        if (new FileInfo(output).Length > new FileInfo(input).Length * (1 - MinimumSavings))
        {
            File.Delete(output);
            throw new NothingToDoException("It's already about as small as it gets.");
        }

        return output;
    }

    /// <summary>The short side becomes 720 px (landscape 1280×720, portrait 720×1280).</summary>
    public static string To720p(string input, string description)
    {
        MediaInfo info = FfmpegRunner.Probe(input);
        if (!info.HasVideo)
            throw new NothingToDoException("There's no video in this file.");
        if (info.ShortSide <= 720)
            throw new NothingToDoException("It's already 720p or smaller.");

        return Encode(input, "720p", ".mp4",
            H264(crf: 23, scale: "scale=w='if(gt(iw,ih),-2,720)':h='if(gt(iw,ih),720,-2)'", audioBitrate: "160k"),
            info, description);
    }

    /// <summary>Drops the sound and copies the picture as it is (no re-encoding, so it's quick and lossless).</summary>
    public static string Mute(string input, string description)
    {
        MediaInfo info = FfmpegRunner.Probe(input);
        if (!info.HasAudio)
            throw new NothingToDoException("It has no sound already.");

        return Encode(input, "muted", Path.GetExtension(input), new[] { "-map", "0", "-map", "-0:a", "-c", "copy" }, info, description);
    }

    private static string[] H264(int crf, string scale, string audioBitrate) => new[]
    {
        "-map", "0:v:0", "-map", "0:a:0?", // the main picture and, if there is one, the main sound
        "-vf", scale,
        "-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(System.Globalization.CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", audioBitrate,
        "-movflags", "+faststart", // playable while it's still downloading
    };

    /// <summary>Reserves "name (suffix).ext" (so nothing is ever overwritten), lets FFmpeg fill it, cleans up on failure.</summary>
    private static string Encode(string input, string? suffix, string extension, string[] encode, MediaInfo info, string description)
    {
        string output = OutputFile.Save(input, suffix, extension, _ => { });
        try
        {
            FfmpegRunner.Run(new[] { "-i", input }.Concat(encode).Append(output).ToArray(), info.Duration, description);
            return output;
        }
        catch
        {
            File.Delete(output);
            throw;
        }
    }
}
