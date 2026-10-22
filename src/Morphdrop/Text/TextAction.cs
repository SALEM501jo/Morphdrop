using Morphdrop.Actions;
using Morphdrop.Documents;
using Morphdrop.Imaging;
using Morphdrop.Pdf;

namespace Morphdrop.Text;

/// <summary>
/// Gets the text out of a file, copies it to the clipboard and saves it as a .txt next to the file. Pictures are read
/// with OCR ("photo (text).txt"; several pictures make one file); PDFs and Word documents give "name.txt".
/// </summary>
internal sealed class TextAction : ICombiningAction
{
    private readonly Func<string, bool> _appliesTo;
    private readonly Func<string, string> _read;
    private readonly string? _suffix;
    private readonly bool _combines;

    private TextAction(string label, string? glyph, string description, Func<string, bool> appliesTo, Func<string, string> read, string? suffix, bool combines)
    {
        Label = label;
        Glyph = glyph;
        Description = description;
        _appliesTo = appliesTo;
        _read = read;
        _suffix = suffix;
        _combines = combines;
    }

    public static TextAction ForPictures(string glyph) => new("Text", glyph, "Copy the text in it",
        path => ImageFormat.FromPath(path) is { CanRead: true } format && format.Kind is not (ImageKind.Pdf or ImageKind.Ico) && OcrReader.IsAvailable,
        TextExtraction.FromPicture, "text", combines: true);

    public static TextAction ForPdfs() => new("TXT", null, "Copy and save its text", PdfToolAction.IsPdf, TextExtraction.FromPdf, null, combines: false);

    public static TextAction ForDocuments() => new("TXT", null, "Copy and save its text",
        path => WordText.Supports(path) || DocumentConverter.CanReadText(path),
        path => WordText.Supports(path) ? WordText.Read(path) : DocumentConverter.ReadText(path), null, combines: false);

    public string Label { get; }

    public string? Glyph { get; }

    public string Description { get; }

    public bool AppliesTo(string path) => _appliesTo(path);

    public string Run(string inputPath) => Finish(inputPath, _read(inputPath));

    public bool Combines(IReadOnlyList<string> inputs) => _combines && inputs.Count > 1;

    public string CombinedDescription(int count) => $"Copy the text of {count} pictures";

    public string RunAll(IReadOnlyList<string> inputs)
    {
        string[] ordered = inputs.Order(ImageConverter.NaturalOrder.Instance).ToArray();
        string text = string.Join(Environment.NewLine + Environment.NewLine, ordered.Select(_read).Where(part => part.Length > 0));
        return Finish(ordered[0], text);
    }

    private string Finish(string inputPath, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new NothingToDoException("No text was found in it.");

        TextExtraction.CopyToClipboard(text);
        return TextExtraction.Save(inputPath, _suffix, text);
    }
}
