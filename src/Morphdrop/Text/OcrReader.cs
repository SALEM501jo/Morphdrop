using System.Runtime.InteropServices.WindowsRuntime;
using ImageMagick;
using Morphdrop.Actions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Morphdrop.Text;

/// <summary>
/// Reads text in pictures with the OCR engine built into Windows: offline, nothing to download. It reads the
/// languages whose OCR pack is installed (most Windows installs have their display language's).
/// </summary>
internal static class OcrReader
{
    private const uint SmallSide = 1200; // smaller pictures are enlarged first: tiny text reads badly

    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    /// <summary>
    /// Reads the picture with every installed OCR language (usually one to three) and keeps the reading that found
    /// the most letters of that language's own script, so an Arabic screenshot isn't read as English or the other way round.
    /// </summary>
    public static string Read(IMagickImage<byte> picture)
    {
        if (OcrEngine.AvailableRecognizerLanguages.Count == 0)
            throw new NothingToDoException("Windows has no text recognition installed. Add it in Settings → Time & language → Language: a language's options → Optical character recognition.");

        using SoftwareBitmap bitmap = ToBitmap(picture);
        string best = "";
        int bestScore = -1;
        foreach (Windows.Globalization.Language language in OcrEngine.AvailableRecognizerLanguages)
        {
            if (OcrEngine.TryCreateFromLanguage(language) is not { } engine)
                continue;

            OcrResult result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
            string text = string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)).Trim();
            int score = Score(text, IsRightToLeftScript(language.LanguageTag));
            if (score > bestScore)
            {
                best = text;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsRightToLeftScript(string tag) =>
        tag.Split('-')[0] is "ar" or "fa" or "ur" or "he" or "yi" or "ps";

    /// <summary>Letters in the script the language is written in: a wrong-language reading scores low.</summary>
    internal static int Score(string text, bool rightToLeft)
    {
        int score = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
                continue;
            bool inRightToLeftScript = c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';
            if (inRightToLeftScript == rightToLeft)
                score++;
        }

        return score;
    }

    private static SoftwareBitmap ToBitmap(IMagickImage<byte> picture)
    {
        using IMagickImage<byte> image = picture.Clone();
        if (image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        image.ColorSpace = ColorSpace.sRGB;
        uint longest = Math.Max(image.Width, image.Height);
        uint limit = OcrEngine.MaxImageDimension;
        if (longest > limit)
            image.Resize(new MagickGeometry(limit, limit));
        else if (longest < SmallSide)
            image.Resize(new Percentage(Math.Min(300, 100.0 * SmallSide / longest)));

        byte[] pixels = image.GetPixels().ToByteArray(PixelMapping.BGRA)!;
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, (int)image.Width, (int)image.Height, BitmapAlphaMode.Ignore);
    }
}
