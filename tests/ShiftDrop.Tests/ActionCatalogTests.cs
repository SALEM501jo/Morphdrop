using ShiftDrop.Actions;
using static ShiftDrop.Tests.TestImages;

namespace ShiftDrop.Tests;

public sealed class ActionCatalogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private static string[] Labels(IReadOnlyList<ActionPage> pages, string page) =>
        pages.Single(p => p.Name == page).Actions.Select(a => a.Label).ToArray();

    [Fact]
    public void APng_GetsEveryOtherFormatAndEveryTool()
    {
        string png = WritePng(Path.Combine(_folder, "a.png"), 2, 2, (_, _) => Red);

        IReadOnlyList<ActionPage> pages = ActionCatalog.For(new[] { png });

        Assert.Equal(new[] { "Convert", "Tools", "Edit" }, pages.Select(p => p.Name));
        Assert.Equal(new[] { "Crop", "Background", "Redact" }, Labels(pages, "Edit"));
        Assert.Equal(new[] { "JPG", "WEBP", "AVIF", "PDF", "ICO", "TIFF", "GIF", "BMP" }, Labels(pages, "Convert"));
        Assert.Equal(new[] { "Compress", "50%", "Max 1920", "Clean" }, Labels(pages, "Tools"));
    }

    [Fact]
    public void AHeicPhoto_CanBeConvertedToAnythingAndResized()
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName("IMG_0001.HEIC");

        Assert.Equal(new[] { "PNG", "JPG", "WEBP", "AVIF", "PDF", "ICO", "TIFF", "GIF", "BMP" }, Labels(pages, "Convert"));
        Assert.Equal(new[] { "50%", "Max 1920" }, Labels(pages, "Tools"));
    }

    [Fact]
    public void AnIcon_HasNoToolsPage()
    {
        Assert.Equal(new[] { "Convert" }, ActionCatalog.ForFileName("app.ico").Select(p => p.Name));
    }

    [Fact]
    public void JpegAliases_CountAsJpg()
    {
        Assert.DoesNotContain("JPG", Labels(ActionCatalog.ForFileName("b.JPEG"), "Convert"));
    }

    [Fact]
    public void FoldersAndUnknownFiles_GetNoActions()
    {
        string text = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(text, "hi");

        Assert.Empty(ActionCatalog.For(new[] { text }));
        Assert.Empty(ActionCatalog.For(new[] { _folder }));
    }
}
