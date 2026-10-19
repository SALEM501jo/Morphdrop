namespace Morphdrop.Tests;

internal static class TestFolder
{
    /// <summary>
    /// Deletes a test's temp folder. Antivirus or the thumbnail indexer may briefly hold a freshly written file,
    /// so this retries, and a folder that still won't go is left for Windows' temp cleanup instead of failing a test.
    /// </summary>
    public static void Delete(string folder)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
