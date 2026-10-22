using System.Text;
using ImageMagick;
using Morphdrop.Actions;
using Morphdrop.Imaging;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Pig = UglyToad.PdfPig;

namespace Morphdrop.Text;

/// <summary>Gets the text out of pictures (OCR) and PDFs (their own text, or OCR for scanned pages).</summary>
internal static class TextExtraction
{
    public static string FromPicture(string path)
    {
        ImageFormat format = ImageReader.Identify(path);
        using MagickImageCollection frames = ImageReader.Read(path, format, allFrames: false);
        return OcrReader.Read(frames[0]);
    }

    /// <summary>The text of every page, in reading order. Pages without text (scans) are read with OCR.</summary>
    public static string FromPdf(string path)
    {
        var pages = new List<string>();
        try
        {
            using Pig.PdfDocument document = Pig.PdfDocument.Open(path);
            foreach (Pig.Content.Page page in document.GetPages())
                pages.Add(Clean(ContentOrderTextExtractor.GetText(page)));
        }
        catch (Exception ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("This PDF is password-protected.", ex);
        }

        if (pages.Any(string.IsNullOrWhiteSpace) && OcrReader.IsAvailable)
        {
            PdfPages.Render(path, (index, _, png) =>
            {
                if (index < pages.Count && string.IsNullOrWhiteSpace(pages[index]))
                {
                    using var image = new MagickImage(png);
                    pages[index] = OcrReader.Read(image);
                }
            });
        }

        return string.Join(Environment.NewLine + Environment.NewLine, pages.Where(page => !string.IsNullOrWhiteSpace(page))).Trim();
    }

    /// <summary>Saves the text as UTF-8 (with a BOM, so every editor reads Arabic and accents right) and returns the path.</summary>
    public static string Save(string sourcePath, string? suffix, string text) =>
        OutputFile.Save(sourcePath, suffix, ".txt", output =>
        {
            byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            output.Write(bytes);
        });

    /// <summary>Puts the text on the clipboard; it stays there after Morphdrop exits. Works from any thread.</summary>
    public static void CopyToClipboard(string text)
    {
        Exception? failure = null;
        bool copied = false;
        var thread = new Thread(() =>
        {
            for (int attempt = 0; attempt < 10 && !copied; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(text, copy: true);
                    copied = true;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    failure = ex; // another app has the clipboard open; try again in a moment
                    Thread.Sleep(50);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (!copied)
            Log.Error("Couldn't copy the text to the clipboard", failure);
    }

    /// <summary>
    /// Turns Arabic presentation forms and ligatures (as some PDFs store them) back into plain letters, and tidies
    /// the line ends.
    /// </summary>
    private static string Clean(string text) =>
        text.Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Trim();
}
