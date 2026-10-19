using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using ImageMagick;
using ShiftDrop.Actions;
using ShiftDrop.Media;

namespace ShiftDrop.Tests;

/// <summary>Runs only where FFmpeg can be found (SHIFTDROP_FFMPEG, ShiftDrop's own copy, or PATH).</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!Ffmpeg.IsAvailable)
            Skip = $"FFmpeg not found; set {Ffmpeg.OverrideVariable} to an ffmpeg.exe to run this test.";
    }
}

internal static class MediaSamples
{
    /// <summary>FFmpeg's moving test pattern, optionally with a 440 Hz tone.</summary>
    public static string Video(string path, int width, int height, double seconds = 2, bool sound = true, int crf = 23)
    {
        var args = new List<string> { "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25:duration={seconds}" };
        if (sound)
            args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}" });
        args.AddRange(new[] { "-c:v", "libx264", "-crf", crf.ToString(), "-pix_fmt", "yuv420p" });
        if (sound)
            args.AddRange(new[] { "-c:a", "aac", "-shortest" });
        Run(args.Append(path));
        return path;
    }

    public static string Tone(string path, double seconds = 2)
    {
        Run(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}", path });
        return path;
    }

    private static void Run(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Ffmpeg.Find()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-hide_banner", "-y" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string log = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, log);
    }
}

public class MediaInfoTests
{
    [Fact]
    public void FfmpegsListing_IsReadForDurationSizeAndStreams()
    {
        const string log = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mov':
              Duration: 00:01:02.50, start: 0.000000, bitrate: 8000 kb/s
              Stream #0:0[0x1](und): Video: hevc (Main) (hvc1 / 0x31637668), yuv420p(tv), 1920x1080, 7800 kb/s, 30 fps
                  Side data:
                    displaymatrix: rotation of -90.00 degrees
              Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 128 kb/s
            """;

        MediaInfo info = FfmpegRunner.ParseInfo(log);

        Assert.Equal(TimeSpan.FromSeconds(62.5), info.Duration);
        Assert.Equal((1080, 1920), (info.Width, info.Height)); // stored sideways, shown upright
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public void AnAudioFile_HasNoVideo()
    {
        MediaInfo info = FfmpegRunner.ParseInfo("  Duration: 00:00:02.00, bitrate: 128 kb/s\n  Stream #0:0: Audio: mp3, 44100 Hz, stereo, fltp, 128 kb/s\n");

        Assert.False(info.HasVideo);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public void MediaFiles_GetTheirOwnSlices()
    {
        static string[] Labels(string file, string page) =>
            ActionCatalog.ForFileName(file).Single(p => p.Name == page).Actions.Select(a => a.Label).ToArray();

        Assert.Equal(new[] { "MP4", "GIF", "WEBP", "MP3", "M4A" }, Labels("clip.mov", "Convert"));
        Assert.Equal(new[] { "GIF", "WEBP", "MP3", "M4A" }, Labels("clip.mp4", "Convert"));
        Assert.Equal(new[] { "Compress", "720p", "Mute" }, Labels("clip.mp4", "Tools"));
        Assert.Equal(new[] { "M4A", "WAV", "FLAC" }, Labels("song.mp3", "Convert"));
        Assert.Contains("MP4", Labels("animation.gif", "Convert"));
        Assert.DoesNotContain(ActionCatalog.ForFileName("song.mp3"), page => page.Name is "Tools" or "Edit");
    }
}

public sealed class FfmpegInstallerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static MemoryStream Zip(string entryName, byte[] content)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(entryName).Open();
            entry.Write(content);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void AVerifiedDownload_InstallsJustFfmpegExe()
    {
        using MemoryStream zip = Zip("ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe", "pretend exe"u8.ToArray());
        string hash = Convert.ToHexString(SHA256.HashData(zip.ToArray()));

        string installed = FfmpegInstaller.InstallFrom(zip, hash, _folder);

        Assert.Equal(Path.Combine(_folder, "ffmpeg.exe"), installed);
        Assert.Equal("pretend exe", File.ReadAllText(installed));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void ADownloadWithTheWrongChecksum_IsNeverInstalled()
    {
        using MemoryStream zip = Zip("x/bin/ffmpeg.exe", "tampered"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => FfmpegInstaller.InstallFrom(zip, Ffmpeg.DownloadSha256, _folder));
        Assert.Empty(Directory.GetFiles(_folder));
    }
}

public sealed class MediaConversionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-media-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string InFolder(string name) => Path.Combine(_folder, name);

    [FfmpegFact]
    public void MovToMp4_KeepsPictureAndSound()
    {
        string mov = MediaSamples.Video(InFolder("clip.mov"), 320, 240);

        string mp4 = MediaConverter.Convert(mov, MediaTarget.Mp4, "test");

        MediaInfo info = FfmpegRunner.Probe(mp4);
        Assert.Equal(InFolder("clip.mp4"), mp4);
        Assert.True(info.HasVideo && info.HasAudio);
        Assert.Equal((320, 240), (info.Width, info.Height));
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.8, 2.2);
    }

    [FfmpegFact]
    public void VideoToGif_Is15FramesASecondAndAtMost640Wide()
    {
        string mp4 = MediaSamples.Video(InFolder("wide.mp4"), 1280, 720, sound: false);

        string gif = MediaConverter.Convert(mp4, MediaTarget.Gif, "test");

        using var frames = new MagickImageCollection(gif);
        Assert.InRange(frames.Count, 28, 32); // 2 s at 15 fps
        Assert.Equal(640u, frames[0].Width);
    }

    [FfmpegFact]
    public void VideoToWebP_IsAnimated()
    {
        string mp4 = MediaSamples.Video(InFolder("clip.mp4"), 320, 240, sound: false);

        string webp = MediaConverter.Convert(mp4, MediaTarget.WebP, "test");

        Assert.True(MagickImages.FrameCount(webp) > 10);
    }

    [FfmpegFact]
    public void VideoToMp3_KeepsOnlyTheSound()
    {
        string mp4 = MediaSamples.Video(InFolder("talk.mp4"), 320, 240);

        MediaInfo info = FfmpegRunner.Probe(MediaConverter.Convert(mp4, MediaTarget.Mp3, "test"));

        Assert.False(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration!.Value.TotalSeconds, 1.8, 2.3);
    }

    [FfmpegFact]
    public void AudioConverts_ToFlacAndM4a()
    {
        string wav = MediaSamples.Tone(InFolder("tone.wav"));

        Assert.True(FfmpegRunner.Probe(MediaConverter.Convert(wav, MediaTarget.Flac, "test")).HasAudio);
        Assert.True(FfmpegRunner.Probe(MediaConverter.Convert(wav, MediaTarget.M4a, "test")).HasAudio);
    }

    [FfmpegFact]
    public void AGif_BecomesAMuchSmallerMp4()
    {
        string gif = MagickImages.Animation(InFolder("blink.gif"), MagickFormat.Gif, "red", "lime", "blue", "yellow");

        MediaInfo info = FfmpegRunner.Probe(MediaConverter.Convert(gif, MediaTarget.Mp4, "test"));

        Assert.True(info.HasVideo);
        Assert.Equal((20, 20), (info.Width, info.Height));
    }

    [FfmpegFact]
    public void Compressing_MakesAHighBitrateVideoSmaller()
    {
        string mp4 = MediaSamples.Video(InFolder("raw.mp4"), 1280, 720, seconds: 3, crf: 5);

        string output = MediaConverter.Compress(mp4, "test");

        Assert.Equal(InFolder("raw (compressed).mp4"), output);
        Assert.True(new FileInfo(output).Length < new FileInfo(mp4).Length / 2);
        Assert.Equal((1280, 720), (FfmpegRunner.Probe(output).Width, FfmpegRunner.Probe(output).Height));
    }

    [FfmpegFact]
    public void To720p_ScalesTheShortSideTo720()
    {
        string landscape = MediaSamples.Video(InFolder("landscape.mp4"), 1920, 1080, seconds: 1, sound: false);
        string portrait = MediaSamples.Video(InFolder("portrait.mp4"), 1080, 1920, seconds: 1, sound: false);
        string small = MediaSamples.Video(InFolder("small.mp4"), 1280, 720, seconds: 1, sound: false);

        MediaInfo wide = FfmpegRunner.Probe(MediaConverter.To720p(landscape, "test"));
        MediaInfo tall = FfmpegRunner.Probe(MediaConverter.To720p(portrait, "test"));

        Assert.Equal((1280, 720), (wide.Width, wide.Height));
        Assert.Equal((720, 1280), (tall.Width, tall.Height));
        Assert.Throws<NothingToDoException>(() => MediaConverter.To720p(small, "test"));
    }

    [FfmpegFact]
    public void Muting_RemovesTheSoundAndKeepsThePicture()
    {
        string mp4 = MediaSamples.Video(InFolder("loud.mp4"), 320, 240);

        string muted = MediaConverter.Mute(mp4, "test");

        MediaInfo info = FfmpegRunner.Probe(muted);
        Assert.Equal(InFolder("loud (muted).mp4"), muted);
        Assert.True(info.HasVideo);
        Assert.False(info.HasAudio);
        Assert.Throws<NothingToDoException>(() => MediaConverter.Mute(muted, "test"));
    }

    [FfmpegFact]
    public void ASilentVideo_HasNoSoundToSave()
    {
        string mp4 = MediaSamples.Video(InFolder("silent.mp4"), 320, 240, sound: false);

        Assert.Throws<NothingToDoException>(() => MediaConverter.Convert(mp4, MediaTarget.Mp3, "test"));
        Assert.False(File.Exists(InFolder("silent.mp3")));
    }

    [FfmpegFact]
    public void ADamagedFile_FailsWithoutLeavingOutput()
    {
        string broken = InFolder("broken.mp4");
        File.WriteAllText(broken, "not a video");

        Assert.ThrowsAny<Exception>(() => MediaConverter.Convert(broken, MediaTarget.Gif, "test"));
        Assert.Single(Directory.GetFiles(_folder));
    }
}
