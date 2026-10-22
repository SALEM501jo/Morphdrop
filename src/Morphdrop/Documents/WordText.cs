using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Morphdrop.Documents;

/// <summary>
/// The text of a Word document (.docx), read straight from its XML, without Word: paragraphs on their own lines,
/// table cells separated by tabs, the body only (no headers, footers or comments).
/// </summary>
internal static class WordText
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".docm";

    public static string Read(string path)
    {
        using ZipArchive package = ZipFile.OpenRead(path);
        ZipArchiveEntry body = package.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("This isn't a Word document Morphdrop can read.");

        var text = new StringBuilder();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        using Stream stream = body.Open();
        using var reader = XmlReader.Create(stream, settings);
        int cellDepth = 0;
        bool firstCellInRow = true;
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NamespaceURI == WordNamespace && reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "t":
                        text.Append(reader.ReadElementContentAsString()); // already moves on to the next node
                        continue;
                    case "tab":
                        text.Append('\t');
                        break;
                    case "br" or "cr":
                        text.Append(cellDepth > 0 ? ' ' : '\n');
                        break;
                    case "tr":
                        firstCellInRow = true;
                        break;
                    case "tc":
                        if (!firstCellInRow)
                            text.Append('\t');
                        firstCellInRow = false;
                        if (!reader.IsEmptyElement)
                            cellDepth++;
                        break;
                }
            }
            else if (reader.NamespaceURI == WordNamespace && reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "p":
                        // Paragraphs inside a table cell stay on the row's line.
                        text.Append(cellDepth > 0 ? ' ' : '\n');
                        break;
                    case "tc":
                        cellDepth--;
                        break;
                    case "tr":
                        text.Append('\n');
                        break;
                }
            }

            reader.Read();
        }

        return Tidy(text.ToString());
    }

    private static string Tidy(string text) =>
        string.Join(Environment.NewLine, text.Replace(" \t", "\t").Split('\n').Select(line => line.Trim(' ').TrimEnd('\t'))).Trim();
}
