using ShiftDrop.Tray;

namespace ShiftDrop.Media;

/// <summary>
/// Gets FFmpeg the first time a video or audio slice is used: asks first (it's a 115 MB download), downloads it once
/// even if several files are dropped meanwhile, then runs whatever was waiting for it.
/// </summary>
internal sealed class FfmpegSetup
{
    private readonly TrayIcon _tray;
    private Task<bool>? _installing;

    public FfmpegSetup(TrayIcon tray) => _tray = tray;

    /// <summary>Call on the UI thread. <paramref name="run"/> runs right away when FFmpeg is there, after installing otherwise.</summary>
    public async void RunWhenReady(Action run)
    {
        if (Ffmpeg.IsAvailable)
        {
            run();
            return;
        }

        if (_installing is null)
        {
            bool download = ConfirmDialog.Ask(
                $"Video and audio need FFmpeg, a free tool that ShiftDrop downloads once (about {Ffmpeg.DownloadSize / 1_000_000} MB, " +
                $"FFmpeg {Ffmpeg.Version} from github.com/GyanD/codexffmpeg). It is checked against a known fingerprint before use.\n\n" +
                "Download it now?",
                "Download", "Not now");
            if (!download)
                return;
            _installing = InstallAsync();
        }

        if (await _installing)
            run();
    }

    private async Task<bool> InstallAsync()
    {
        _tray.ShowMessage("Downloading FFmpeg…", $"About {Ffmpeg.DownloadSize / 1_000_000} MB, once. Your file is handled as soon as it's done.");
        var progress = new Progress<double>(share => _tray.SetStatus($"Downloading FFmpeg: {share:P0}"));
        try
        {
            string path = await FfmpegInstaller.InstallAsync(progress);
            Log.Info($"FFmpeg {Ffmpeg.Version} installed at {path}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not install FFmpeg", ex);
            _tray.ShowError("Couldn't download FFmpeg", ex.Message);
            _installing = null; // let the next drop try again
            return false;
        }
        finally
        {
            _tray.SetStatus(null);
        }
    }
}
