namespace ShiftDrop.Actions;

internal static class OutputFile
{
    /// <summary>
    /// Writes a new file next to <paramref name="inputPath"/>, named after it plus an optional "(suffix)", and
    /// returns its path. A failed write leaves nothing behind.
    /// </summary>
    public static string Save(string inputPath, string? suffix, string extension, Action<Stream> write)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        string baseName = Path.GetFileNameWithoutExtension(inputPath) + (suffix is null ? "" : $" ({suffix})");
        string path;
        using (FileStream output = CreateUnique(directory, baseName, extension, out path))
        {
            try
            {
                write(output);
            }
            catch
            {
                output.Dispose();
                TryDelete(path);
                throw;
            }
        }

        return path;
    }

    /// <summary>
    /// Creates "name.ext", or "name (1).ext", "name (2).ext"… if that is taken.
    /// Existing files are never overwritten: the file is opened with <see cref="FileMode.CreateNew"/>.
    /// </summary>
    public static FileStream CreateUnique(string directory, string baseName, string extension, out string path)
    {
        for (int copy = 0; copy < 10_000; copy++)
        {
            path = Path.Combine(directory, copy == 0 ? baseName + extension : $"{baseName} ({copy}){extension}");
            if (File.Exists(path) || Directory.Exists(path))
                continue;

            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Someone created it a moment ago; try the next name.
            }
        }

        throw new IOException($"No free file name for {baseName}{extension} in {directory}.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave the partial file; the error is reported anyway.
        }
    }
}
