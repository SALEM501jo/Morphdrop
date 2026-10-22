using ImageMagick;
using ImageMagick.Drawing;
using Morphdrop.Actions;
using Morphdrop.Pdf;
using Morphdrop.Text;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Morphdrop.Tests;

/// <summary>Runs only where Windows has a text-recognition (OCR) language installed.</summary>
public sealed class OcrFactAttribute : FactAttribute
{
    public OcrFactAttribute()
    {
        if (!OcrReader.IsAvailable)
            Skip = "Windows has no OCR language installed.";
    }
}

public sealed class TextTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-text-").FullName;

    static TextTests() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    /// <summary>A picture of dark text on white, like a screenshot.</summary>
    private string Screenshot(string name, params string[] lines)
    {
        using var image = new MagickImage(MagickColors.White, 1400, (uint)(120 + lines.Length * 90));
        var drawables = new Drawables().Font("Arial").FontPointSize(56).FillColor(MagickColors.Black);
        for (int i = 0; i < lines.Length; i++)
            drawables.Text(60, 110 + i * 90, lines[i]);
        drawables.Draw(image);
        string path = InFolder(name);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    [OcrFact]
    public void TheTextInAPicture_IsRead()
    {
        string picture = Screenshot("note.png", "Meeting at 10:30", "Bring the Morphdrop notes");

        string text = TextExtraction.FromPicture(picture);

        Assert.Contains("Meeting", text);
        Assert.Contains("Morphdrop", text);
    }

    [OcrFact]
    public void ArabicText_IsReadWithTheArabicLanguage()
    {
        if (!Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Any(language => language.LanguageTag.StartsWith("ar", StringComparison.OrdinalIgnoreCase)))
            return; // Arabic OCR isn't installed here

        string picture = InFolder("arabic.png");
        Sta.Run(() =>
        {
            // WPF joins Arabic letters properly (ImageMagick's text drawing doesn't).
            var text = Morphdrop.Pdf.PdfText.Format("مرحبا بكم في المدينة", 1200, 64, Morphdrop.Pdf.PdfText.SemiBold);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, 1400, 300));
                Morphdrop.Pdf.PdfText.Draw(context, text, new System.Windows.Rect(100, 100, 1200, 120));
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1400, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(picture);
            encoder.Save(file);
        });

        string read = TextExtraction.FromPicture(picture);

        Assert.Contains("المدينة", read);
    }

    [Fact]
    public void AReadingInTheWrongScript_ScoresLow()
    {
        Assert.True(OcrReader.Score("مرحبا", rightToLeft: true) > OcrReader.Score("مرحبا", rightToLeft: false));
        Assert.True(OcrReader.Score("Hello", rightToLeft: false) > OcrReader.Score("Hello", rightToLeft: true));
    }

    [OcrFact]
    public void ThePictureTextSlice_SavesATextFileNextToIt()
    {
        string picture = Screenshot("note.png", "Invoice number 4821");

        string saved = TextAction.ForPictures("x").Run(picture);

        Assert.Equal(InFolder("note (text).txt"), saved);
        Assert.Contains("4821", File.ReadAllText(saved));
    }

    [Fact]
    public void APdfsOwnText_IsReadPageByPage()
    {
        string pdf = InFolder("report.pdf");
        using (var document = new PdfDocument())
        {
            foreach (string line in new[] { "First page about apples", "Second page about pears" })
            {
                using XGraphics graphics = XGraphics.FromPdfPage(document.AddPage());
                graphics.DrawString(line, new XFont("Arial", 20), XBrushes.Black, new XPoint(72, 100));
            }

            document.Save(pdf);
        }

        string text = TextExtraction.FromPdf(pdf);

        Assert.Contains("First page about apples", text);
        Assert.Contains("Second page about pears", text);
        Assert.True(text.IndexOf("apples", StringComparison.Ordinal) < text.IndexOf("pears", StringComparison.Ordinal));
    }

    [OcrFact]
    public void AScannedPdf_IsReadWithOcr()
    {
        string scan = Screenshot("scan.png", "Scanned contract page", "Signed in Amman");
        using (PdfDocument document = PictureDocument.Create(new[] { new PicturePage(scan) }, PageOptions.Default))
            document.Save(InFolder("scan.pdf"));

        string text = TextExtraction.FromPdf(InFolder("scan.pdf"));

        Assert.Contains("contract", text);
    }

    [Fact]
    public void APdfWithoutAnyText_HasNothingToCopy()
    {
        string pdf = InFolder("blank.pdf");
        using (var document = new PdfDocument())
        {
            using (XGraphics graphics = XGraphics.FromPdfPage(document.AddPage()))
                graphics.DrawRectangle(XBrushes.White, 0, 0, 10, 10);
            document.Save(pdf);
        }

        Assert.Throws<NothingToDoException>(() => TextAction.ForPdfs().Run(pdf));
    }

    [Fact]
    public void SavedText_IsUtf8WithABom_SoArabicOpensRightEverywhere()
    {
        string source = InFolder("doc.pdf");
        File.WriteAllText(source, "");

        string saved = TextExtraction.Save(source, null, "مرحبا");

        byte[] bytes = File.ReadAllBytes(saved);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("مرحبا", File.ReadAllText(saved));
    }
}
