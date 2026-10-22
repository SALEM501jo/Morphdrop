using System.IO.Compression;
using Morphdrop.Actions;
using Morphdrop.Documents;

namespace Morphdrop.Tests;

/// <summary>Runs only where that Office program is installed.</summary>
public sealed class OfficeFactAttribute : FactAttribute
{
    public OfficeFactAttribute(string app)
    {
        if (!DocumentConverter.IsInstalled(Enum.Parse<OfficeApp>(app)))
            Skip = $"Microsoft {app} isn't installed.";
    }
}

internal static class DocumentSamples
{
    /// <summary>The smallest Word document there is: one paragraph.</summary>
    public static string Docx(string path, string text)
    {
        WritePackage(path, new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """,
            ["_rels/.rels"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """,
            ["word/document.xml"] = $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body>
                </w:document>
                """,
        });
        return path;
    }

    /// <summary>A workbook with one sheet and one number in A1.</summary>
    public static string Xlsx(string path)
    {
        WritePackage(path, new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """,
            ["_rels/.rels"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """,
            ["xl/workbook.xml"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """,
            ["xl/_rels/workbook.xml.rels"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """,
            ["xl/worksheets/sheet1.xml"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData><row r="1"><c r="A1"><v>42</v></c></row></sheetData>
                </worksheet>
                """,
        });
        return path;
    }

    private static void WritePackage(string path, Dictionary<string, string> parts)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in parts)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }
}

public sealed class DocumentTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-docs-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    [Theory]
    [InlineData("report.docx", "Word")]
    [InlineData("old.DOC", "Word")]
    [InlineData("notes.odt", "Word")]
    [InlineData("budget.xlsx", "Excel")]
    [InlineData("slides.pptx", "PowerPoint")]
    public void OfficeDocuments_GoToTheirOwnProgram(string name, string app) =>
        Assert.Equal(Enum.Parse<OfficeApp>(app), DocumentConverter.AppFor(name));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("photo.jpg")]
    [InlineData("paper.pdf")]
    public void OtherFiles_AreNotDocuments(string name) => Assert.Null(DocumentConverter.AppFor(name));

    [OfficeFact("Word")]
    public void AWordDocument_GetsAPdfSlice()
    {
        ActionPage convert = ActionCatalog.ForFileName("report.docx").Single(page => page.Name == "Convert");
        Assert.Contains(convert.Actions, action => action is DocumentToPdfAction);
    }

    [OfficeFact("Word")]
    public void Docx_BecomesAPdfNextToIt()
    {
        string input = DocumentSamples.Docx(Path.Combine(_folder, "letter.docx"), "Hello from Morphdrop");

        string output = DocumentConverter.ToPdf(input);

        Assert.Equal(Path.Combine(_folder, "letter.pdf"), output);
        AssertIsPdf(output);
    }

    [OfficeFact("Excel")]
    public void Xlsx_BecomesAPdf()
    {
        string input = DocumentSamples.Xlsx(Path.Combine(_folder, "sheet.xlsx"));

        AssertIsPdf(DocumentConverter.ToPdf(input));
    }

    [OfficeFact("Word")]
    public void ADamagedDocument_FailsWithoutLeavingAnEmptyPdf()
    {
        string input = Path.Combine(_folder, "broken.docx");
        File.WriteAllText(input, "this is not a Word file");

        Assert.ThrowsAny<Exception>(() => DocumentConverter.ToPdf(input));
        Assert.False(File.Exists(Path.Combine(_folder, "broken.pdf")));
    }

    private static void AssertIsPdf(string path)
    {
        byte[] start = File.ReadAllBytes(path)[..5];
        Assert.Equal("%PDF-"u8.ToArray(), start);
    }
}
