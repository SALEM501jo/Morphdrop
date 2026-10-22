using Morphdrop.Actions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Morphdrop.Pdf;

/// <summary>Locks a PDF with a password (AES-256) or saves an unlocked copy of a locked one.</summary>
internal static class PdfSecurity
{
    /// <summary>True when the PDF can't be opened without a password.</summary>
    public static bool IsLocked(string path)
    {
        try
        {
            using PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            return false;
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    /// <summary>Saves "name (locked).pdf", which asks for <paramref name="password"/> before it opens.</summary>
    public static string Lock(string path, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("The password can't be empty.", nameof(password));

        using PdfDocument document = PdfTools.Open(path, PdfDocumentOpenMode.Modify);
        document.SecuritySettings.UserPassword = password;
        document.SecuritySettings.OwnerPassword = password;
        document.SecurityHandler.SetEncryptionToV5(); // AES-256
        return OutputFile.Save(path, "locked", ".pdf", output => document.Save(output, closeStream: false));
    }

    /// <summary>True when <paramref name="password"/> opens the locked PDF.</summary>
    public static bool CanOpen(string path, string password)
    {
        try
        {
            using PdfDocument document = PdfReader.Open(path, password, PdfDocumentOpenMode.Modify);
            return true;
        }
        catch (PdfReaderException)
        {
            return false;
        }
    }

    /// <summary>Saves "name (unlocked).pdf", which opens without a password.</summary>
    public static string Unlock(string path, string password)
    {
        PdfDocument document;
        try
        {
            document = PdfReader.Open(path, password, PdfDocumentOpenMode.Modify);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidDataException("That password doesn't open this PDF.", ex);
        }

        using (document)
        {
            document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
            return OutputFile.Save(path, "unlocked", ".pdf", output => document.Save(output, closeStream: false));
        }
    }
}
