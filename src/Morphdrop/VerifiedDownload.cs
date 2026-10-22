using System.Net.Http;
using System.Security.Cryptography;

namespace Morphdrop;

/// <summary>Downloads a file next to its final place, checks its SHA-256, and only then moves it into place.</summary>
internal static class VerifiedDownload
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task DownloadAsync(Uri url, string target, long expectedSize, string expectedSha256,
        IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string partial = target + ".part";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? expectedSize;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[1 << 16];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(Math.Min(1, (double)received / total));
                }
            }

            Verify(hash.GetHashAndReset(), expectedSha256, Path.GetFileName(target));
            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    internal static void Verify(byte[] actual, string expectedSha256, string name)
    {
        if (!string.Equals(Convert.ToHexString(actual), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The download of {name} didn't match its expected checksum, so it wasn't used. Please try again later.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Morphdrop/{typeof(VerifiedDownload).Assembly.GetName().Version?.ToString(3)}");
        return client;
    }
}
