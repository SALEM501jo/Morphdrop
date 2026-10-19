using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Morphdrop.Media;

/// <summary>Downloads FFmpeg, verifies its SHA-256, and keeps only ffmpeg.exe from the zip.</summary>
internal static class FfmpegInstaller
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<string> InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Ffmpeg.InstallFolder);
        string zipPath = Path.Combine(Ffmpeg.InstallFolder, "download.zip.part");
        try
        {
            using (HttpResponseMessage response = await Http.GetAsync(Ffmpeg.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? Ffmpeg.DownloadSize;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[1 << 16];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report((double)received / total);
                }
            }

            await using var zip = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return InstallFrom(zip, Ffmpeg.DownloadSha256, Ffmpeg.InstallFolder);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>Checks the zip against the expected hash, then extracts its bin\ffmpeg.exe into the folder.</summary>
    internal static string InstallFrom(Stream zip, string expectedSha256, string folder)
    {
        string actual = Convert.ToHexString(SHA256.HashData(zip));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The FFmpeg download didn't match its expected checksum, so it wasn't used. Please try again later.");

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The FFmpeg download doesn't contain ffmpeg.exe.");

        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "ffmpeg.exe");
        string partial = target + ".part";
        entry.ExtractToFile(partial, overwrite: true);
        File.Move(partial, target, overwrite: true);
        return target;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Morphdrop/{typeof(FfmpegInstaller).Assembly.GetName().Version?.ToString(3)}");
        return client;
    }
}
