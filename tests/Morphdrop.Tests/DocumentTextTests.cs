using System.IO.Compression;
using System.Text;
using Morphdrop.Actions;
using Morphdrop.Documents;

namespace Morphdrop.Tests;

public sealed class DocumentTextTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-doctext-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    private static void Package(string path, Dictionary<string, string> parts)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in parts)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }

    private string Docx(string name, string body)
    {
        string path = InFolder(name);
        Package(path, new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""",
            ["_rels/.rels"] = """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""",
            ["word/document.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}</w:body></w:document>""",
        });
        return path;
    }

    private static string P(params string[] runs) => "<w:p>" + string.Concat(runs.Select(run => $"<w:r><w:t xml:space=\"preserve\">{run}</w:t></w:r>")) + "</w:p>";

    private string Xlsx(string name, params (string Sheet, string[][] Rows)[] sheets)
    {
        string path = InFolder(name);
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>"""
                + string.Concat(sheets.Select((_, i) => $"""<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""")) + "</Types>",
            ["_rels/.rels"] = """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>"""
                + string.Concat(sheets.Select((sheet, i) => $"""<sheet name="{sheet.Sheet}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""")) + "</sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + string.Concat(sheets.Select((_, i) => $"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>""")) + "</Relationships>",
        };
        for (int i = 0; i < sheets.Length; i++)
        {
            var rows = new StringBuilder();
            for (int r = 0; r < sheets[i].Rows.Length; r++)
            {
                rows.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < sheets[i].Rows[r].Length; c++)
                {
                    string cell = $"{(char)('A' + c)}{r + 1}";
                    string value = sheets[i].Rows[r][c];
                    rows.Append(double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _)
                        ? $"<c r=\"{cell}\"><v>{value}</v></c>"
                        : $"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>");
                }

                rows.Append("</row>");
            }

            parts[$"xl/worksheets/sheet{i + 1}.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>{rows}</sheetData></worksheet>""";
        }

        Package(path, parts);
        return path;
    }

    [Fact]
    public void AWordDocumentsText_IsReadWithoutWord()
    {
        string docx = Docx("letter.docx",
            P("Dear ", "team,") + P("مرحبا بالجميع") +
            "<w:tbl><w:tr><w:tc>" + P("Item") + "</w:tc><w:tc>" + P("Price") + "</w:tc></w:tr>" +
            "<w:tr><w:tc>" + P("Coffee") + "</w:tc><w:tc>" + P("2.50") + "</w:tc></w:tr></w:tbl>" +
            P("Thanks"));

        string text = WordText.Read(docx);

        string[] lines = text.Split(Environment.NewLine);
        Assert.Equal(new[] { "Dear team,", "مرحبا بالجميع", "Item\tPrice", "Coffee\t2.50", "Thanks" }, lines);
    }

    [Fact]
    public void TheWordTxtSlice_SavesNameTxt()
    {
        string docx = Docx("notes.docx", P("Hello"));

        string saved = Morphdrop.Text.TextAction.ForDocuments().Run(docx);

        Assert.Equal(InFolder("notes.txt"), saved);
        Assert.Equal("Hello", File.ReadAllText(saved));
    }

    [Fact]
    public void AOneSheetWorkbook_BecomesOneCsv_QuotedWhereNeeded()
    {
        string xlsx = Xlsx("prices.xlsx", ("Prices", new[]
        {
            new[] { "Item", "Price", "Note" },
            new[] { "Coffee", "2.5", "hot, fresh" },
            new[] { "قهوة", "3", "say \"hi\"" },
        }));

        string csv = SpreadsheetCsv.Convert(xlsx);

        Assert.Equal(InFolder("prices.csv"), csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(csv)[..3]);
        Assert.Equal(new[] { "Item,Price,Note", "Coffee,2.5,\"hot, fresh\"", "قهوة,3,\"say \"\"hi\"\"\"", "" }, File.ReadAllText(csv).Split("\r\n"));
    }

    [Fact]
    public void SeveralSheets_BecomeAFolderWithACsvEach()
    {
        string xlsx = Xlsx("year.xlsx", ("January", new[] { new[] { "a", "1" } }), ("February", new[] { new[] { "b", "2" } }));

        string folder = SpreadsheetCsv.Convert(xlsx);

        Assert.Equal(InFolder("year (sheets)"), folder);
        Assert.Equal(new[] { "February.csv", "January.csv" }, Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToArray()!);
    }

    [Fact]
    public void WordAndExcelFiles_GetTheirSlices()
    {
        Assert.Contains(ActionCatalog.ForFileName("a.docx")[0].Actions, action => action.Label == "TXT");
        Assert.Contains(ActionCatalog.ForFileName("a.xlsx")[0].Actions, action => action.Label == "CSV");
        Assert.DoesNotContain(ActionCatalog.ForFileName("a.xlsx")[0].Actions, action => action.Label == "TXT");
    }

    [Fact]
    public void CsvQuoting_FollowsTheStandard()
    {
        Assert.Equal("plain", SpreadsheetCsv.Quote("plain"));
        Assert.Equal("\"a,b\"", SpreadsheetCsv.Quote("a,b"));
        Assert.Equal("\"line\nbreak\"", SpreadsheetCsv.Quote("line\nbreak"));
    }
}
