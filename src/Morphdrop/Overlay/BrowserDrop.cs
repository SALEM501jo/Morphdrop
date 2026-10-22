using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Morphdrop.Actions;
using Morphdrop.Archives;
using DataObject = System.Windows.IDataObject;

namespace Morphdrop.Overlay;

/// <summary>
/// What a browser drags instead of a file: a picture (a "virtual file", which the browser hands over on drop), or a
/// link to a file. Either is saved to the Downloads folder on drop and then handled like any dropped file. Links are
/// fetched without the browser's sign-in or cookies.
/// </summary>
internal sealed class BrowserDrop
{
    private const long MaxDownload = 2L * 1024 * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();

    private BrowserDrop(string fileName, Uri? link)
    {
        FileName = fileName;
        Link = link;
    }

    /// <summary>The name the file will get, which also decides the wheel's slices.</summary>
    public string FileName { get; }

    /// <summary>Set for a link that is downloaded after the drop; null for a picture the browser hands over.</summary>
    public Uri? Link { get; }

    /// <summary>Recognizes a browser drag, or returns null (text, or a link to a web page rather than a file).</summary>
    public static BrowserDrop? Read(DataObject data)
    {
        try
        {
            if (VirtualFileName(data) is { } name)
                return new BrowserDrop(Sanitize(name), null);

            // Chrome's downloads: "image/png:photo.png:https://…".
            if (data.GetData("DownloadURL") is string download && download.Split(':', 3) is [_, var downloadName, var url]
                && Uri.TryCreate(url, UriKind.Absolute, out Uri? downloadUri) && IsWeb(downloadUri))
                return new BrowserDrop(Sanitize(downloadName), downloadUri);

            foreach (string format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", "text/x-moz-url", "Text" })
            {
                if (ReadText(data, format) is { } text && Uri.TryCreate(text.Split('\n')[0].Trim(), UriKind.Absolute, out Uri? uri) && IsWeb(uri)
                    && Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)) is { Length: > 0 } linked && IsFile(linked))
                    return new BrowserDrop(Sanitize(linked), uri);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            Log.Error("Couldn't read the browser drag", ex);
        }

        return null;
    }

    /// <summary>
    /// Call inside the drop (the browser's data is only there until it returns). A picture is written at once; a link
    /// is downloaded afterwards by the returned task.
    /// </summary>
    public Task<string> SaveAsync(DataObject data) => SaveAsync(data, OutputFile.Folder ?? DownloadsFolder());

    internal Task<string> SaveAsync(DataObject data, string folder)
    {
        if (Link is null)
        {
            byte[] content = ReadFileContents(data, 0) ?? throw new InvalidDataException("The browser didn't hand over the picture.");
            return Task.FromResult(Write(folder, FileName, content));
        }

        Uri link = Link;
        string name = FileName;
        return Task.Run(async () =>
        {
            using HttpResponseMessage response = await Http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxDownload)
                throw new InvalidDataException("The file is too big to download here.");
            Directory.CreateDirectory(folder);
            using FileStream output = OutputFile.CreateUnique(folder, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name), out string saved);
            await using Stream body = await response.Content.ReadAsStreamAsync();
            await body.CopyToAsync(output);
            return saved;
        });
    }

    public static string DownloadsFolder()
    {
        try
        {
            return SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero);
        }
        catch (COMException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    private static string Write(string folder, string name, byte[] content)
    {
        Directory.CreateDirectory(folder);
        using FileStream output = OutputFile.CreateUnique(folder, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name), out string path);
        output.Write(content);
        return path;
    }

    /// <summary>A link counts as a file when Morphdrop has slices for its type (beyond zipping it).</summary>
    private static bool IsFile(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is not ("" or ".html" or ".htm" or ".php" or ".asp" or ".aspx" or ".jsp")
        && ActionCatalog.ForFileName(name).SelectMany(page => page.Actions).Any(action => action is not ArchiveAction);

    private static bool IsWeb(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;

    private static string Sanitize(string name)
    {
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return safe.Length == 0 ? "download" : safe;
    }

    private static string? ReadText(DataObject data, string format)
    {
        if (!data.GetDataPresent(format))
            return null;
        return data.GetData(format) switch
        {
            string text => text,
            MemoryStream stream => format == "UniformResourceLocator"
                ? Encoding.Default.GetString(stream.ToArray()).TrimEnd('\0')
                : Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0'),
            _ => null,
        };
    }

    /// <summary>The first name in a FILEGROUPDESCRIPTORW (how browsers and Outlook describe the files they hand over).</summary>
    private static string? VirtualFileName(DataObject data)
    {
        if (!data.GetDataPresent("FileGroupDescriptorW") || data.GetData("FileGroupDescriptorW") is not MemoryStream descriptor)
            return null;

        byte[] bytes = descriptor.ToArray();
        const int nameOffset = 4 + 72; // after the item count and the descriptor's fields before cFileName
        if (bytes.Length < nameOffset + 2 || BitConverter.ToUInt32(bytes, 0) == 0)
            return null;
        string name = Encoding.Unicode.GetString(bytes, nameOffset, Math.Min(520, bytes.Length - nameOffset));
        int end = name.IndexOf('\0');
        name = end >= 0 ? name[..end] : name;
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// The content of handed-over file number <paramref name="index"/>. WPF's GetData asks for "all of them" (index
    /// −1), which browsers refuse, so this asks through COM for the one that is wanted.
    /// </summary>
    private static byte[]? ReadFileContents(DataObject data, int index)
    {
        if (data is not IDataObject com)
            return (data.GetData("FileContents") as MemoryStream)?.ToArray();

        var format = new FORMATETC
        {
            cfFormat = unchecked((short)RegisterClipboardFormat("FileContents")),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            ptd = IntPtr.Zero,
            tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
        };
        com.GetData(ref format, out STGMEDIUM medium);
        try
        {
            if (medium.tymed == TYMED.TYMED_ISTREAM)
            {
                var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                using var copy = new MemoryStream();
                var buffer = new byte[1 << 16];
                IntPtr read = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    while (true)
                    {
                        stream.Read(buffer, buffer.Length, read);
                        int count = Marshal.ReadInt32(read);
                        if (count <= 0)
                            break;
                        copy.Write(buffer, 0, count);
                        if (copy.Length > MaxDownload)
                            throw new InvalidDataException("The file is too big.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(read);
                    Marshal.ReleaseComObject(stream);
                }

                return copy.ToArray();
            }

            if (medium.tymed == TYMED.TYMED_HGLOBAL)
            {
                int size = (int)GlobalSize(medium.unionmember);
                IntPtr pointer = GlobalLock(medium.unionmember);
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    return bytes;
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }

            return null;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static HttpClient CreateClient()
    {
        // No cookies and no sign-in: only what anyone could download from the link.
        var client = new HttpClient(new HttpClientHandler { UseCookies = false, UseDefaultCredentials = false }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Morphdrop/{typeof(BrowserDrop).Assembly.GetName().Version?.ToString(3)}");
        return client;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.LPWStr)]
    private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token);
}
