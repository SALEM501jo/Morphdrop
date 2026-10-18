namespace ShiftDrop.Actions;

internal static class OutputFile
{
    /// <summary>
    /// Creates "name.ext" next to the input, or "name (1).ext", "name (2).ext"… if that is taken.
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
}
