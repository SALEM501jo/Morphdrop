using System.IO.Compression;
using Morphdrop.Actions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Morphdrop.Archives;

/// <summary>Packs files and folders into a ZIP, and unpacks ZIP, 7Z, RAR and TAR archives.</summary>
internal static class Archives
{
    private static readonly string[] ArchiveExtensions =
        { ".zip", ".7z", ".rar", ".tar", ".tgz", ".tbz2", ".txz", ".gz", ".bz2", ".xz" };

    /// <summary>Judged by the name only, like the wheel's other slices (the file needn't exist yet).</summary>
    public static bool IsArchive(string path)
    {
        string name = Path.GetFileName(path);
        return !Directory.Exists(path) && ArchiveExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One ZIP with everything: "name.zip" for one file or folder (a folder keeps its own name inside, like Windows'
    /// "Compressed (zipped) folder"), "first name (N items).zip" for several.
    /// </summary>
    public static string Zip(IReadOnlyList<string> paths)
    {
        string first = paths[0];
        string? suffix = paths.Count > 1 ? $"{paths.Count} items" : null;
        string sourceForName = Directory.Exists(first) ? first.TrimEnd(Path.DirectorySeparatorChar) : first;
        return OutputFile.Save(sourceForName, suffix, ".zip", output =>
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: System.Text.Encoding.UTF8);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                    AddFolder(zip, path.TrimEnd(Path.DirectorySeparatorChar), Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), used);
                else
                    AddFile(zip, path, Path.GetFileName(path), used);
            }
        });
    }

    private static void AddFolder(ZipArchive zip, string folder, string name, HashSet<string> used)
    {
        string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        string[] folders = Directory.GetDirectories(folder, "*", SearchOption.AllDirectories);
        if (files.Length == 0 && folders.Length == 0)
            zip.CreateEntry(name + "/");

        foreach (string sub in folders.Where(sub => Directory.GetFileSystemEntries(sub).Length == 0))
            zip.CreateEntry($"{name}/{Path.GetRelativePath(folder, sub).Replace('\\', '/')}/");
        foreach (string file in files)
            AddFile(zip, file, $"{name}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}", used);
    }

    private static void AddFile(ZipArchive zip, string path, string entryName, HashSet<string> used)
    {
        string name = entryName;
        for (int copy = 1; !used.Add(name); copy++)
            name = $"{Path.GetFileNameWithoutExtension(entryName)} ({copy}){Path.GetExtension(entryName)}";

        // Already-compressed files (photos, videos, archives) gain nothing from deflate; store them quickly.
        CompressionLevel level = IsCompressed(path) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
        zip.CreateEntryFromFile(path, name, level);
    }

    private static bool IsCompressed(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".avif" or ".heic" or ".gif" or ".mp4" or ".mov" or ".mkv" or ".webm"
        or ".mp3" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".flac" or ".zip" or ".7z" or ".rar" or ".gz" or ".docx"
        or ".xlsx" or ".pptx";

    /// <summary>
    /// Unpacks into a folder named after the archive, next to it. When everything inside sits in one folder of that
    /// same name, it isn't nested twice. Entries that would land outside the folder ("zip slip") are refused.
    /// </summary>
    public static string Extract(string archivePath)
    {
        string folder = OutputFile.CreateUniqueFolder(BaseNamePath(archivePath), suffix: null);
        try
        {
            int count = ExtractInto(archivePath, folder);
            if (count == 0)
                throw new NothingToDoException("The archive is empty.");
            FlattenSameNamedRoot(folder);
            return folder;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    /// <summary>"photos.tar.gz" names its folder "photos", not "photos.tar".</summary>
    private static string BaseNamePath(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        foreach (string double_ in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
        {
            if (name.EndsWith(double_, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Path.GetDirectoryName(archivePath)!, name[..^double_.Length] + ".x");
        }

        return archivePath;
    }

    private static int ExtractInto(string archivePath, string folder)
    {
        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        int count = 0;
        try
        {
            if (IsCompressedTar(archivePath))
            {
                // A .tar.gz is a stream inside a stream: read it from start to end.
                using FileStream stream = File.OpenRead(archivePath);
                using IReader reader = ReaderFactory.Open(stream);
                while (reader.MoveToNextEntry())
                {
                    if (Write(reader.Entry.Key, reader.Entry.IsDirectory, root, reader.OpenEntryStream))
                        count++;
                }
            }
            else
            {
                using IArchive archive = ArchiveFactory.Open(archivePath);
                foreach (IArchiveEntry entry in archive.Entries)
                {
                    if (entry.IsEncrypted)
                        throw new InvalidDataException("This archive is password-protected.");
                    if (Write(entry.Key, entry.IsDirectory, root, entry.OpenEntryStream))
                        count++;
                }
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("This archive is password-protected.", ex);
        }

        return count;
    }

    private static bool IsCompressedTar(string path)
    {
        string name = Path.GetFileName(path);
        return new[] { ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz" }
            .Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Write(string? key, bool isDirectory, string root, Func<Stream> open)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        string target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The archive tries to write outside its folder ({key}); nothing was extracted.");

        if (isDirectory)
        {
            Directory.CreateDirectory(target);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using Stream source = open();
        using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write);
        source.CopyTo(output);
        return true;
    }

    private static void FlattenSameNamedRoot(string folder)
    {
        string[] entries = Directory.GetFileSystemEntries(folder);
        if (entries.Length != 1 || !Directory.Exists(entries[0]))
            return;
        if (!string.Equals(Path.GetFileName(entries[0]), Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(folder).StartsWith(Path.GetFileName(entries[0]) + " (", StringComparison.OrdinalIgnoreCase))
            return;

        string inner = entries[0];
        foreach (string entry in Directory.GetFileSystemEntries(inner))
        {
            string destination = Path.Combine(folder, Path.GetFileName(entry));
            if (Directory.Exists(entry))
                Directory.Move(entry, destination);
            else
                File.Move(entry, destination);
        }

        Directory.Delete(inner);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; the error is reported anyway.
        }
    }
}
