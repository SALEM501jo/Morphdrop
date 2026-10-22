using Morphdrop.Actions;
using static Morphdrop.Tests.TestImages;

namespace Morphdrop.Tests;

public sealed class ActionCatalogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private static string[] Labels(IReadOnlyList<ActionPage> pages, string page) =>
        pages.Single(p => p.Name == page).Actions.Select(a => a.Label).ToArray();

    [Fact]
    public void APng_GetsEveryOtherFormatAndEveryTool()
    {
        string png = WritePng(Path.Combine(_folder, "a.png"), 2, 2, (_, _) => Red);

        IReadOnlyList<ActionPage> pages = ActionCatalog.For(new[] { png });

        Assert.Equal(new[] { "Convert", "Tools", "Edit" }, pages.Select(p => p.Name));
        Assert.Equal(new[] { "Crop", "Background", "Redact", "Remove BG", "PDF + text" }, Labels(pages, "Edit"));
        Assert.Equal(new[] { "JPG", "WEBP", "AVIF", "PDF", "ICO", "TIFF", "GIF", "BMP", "JXL" }, Labels(pages, "Convert"));
        Assert.Equal(new[] { "Compress", "50%", "Max 1920", "Clean", "Text", "ZIP" }, Labels(pages, "Tools"));
    }

    [Fact]
    public void AHeicPhoto_CanBeConvertedToAnythingAndResized()
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName("IMG_0001.HEIC");

        Assert.Equal(new[] { "PNG", "JPG", "WEBP", "AVIF", "PDF", "ICO", "TIFF", "GIF", "BMP", "JXL" }, Labels(pages, "Convert"));
        Assert.Equal(new[] { "50%", "Max 1920", "Text", "ZIP" }, Labels(pages, "Tools"));
    }

    [Fact]
    public void AnIcon_GetsNoImageTools_OnlyZip()
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName("app.ico");

        Assert.Equal(new[] { "Convert", "Tools" }, pages.Select(p => p.Name));
        Assert.Equal(new[] { "ZIP" }, Labels(pages, "Tools"));
    }

    [Fact]
    public void JpegAliases_CountAsJpg()
    {
        Assert.DoesNotContain("JPG", Labels(ActionCatalog.ForFileName("b.JPEG"), "Convert"));
    }

    [Fact]
    public void FoldersAndOtherFiles_CanStillBeZipped()
    {
        string text = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(text, "hi");

        Assert.Equal(new[] { "ZIP" }, ActionCatalog.For(new[] { text }).SelectMany(page => page.Actions).Select(action => action.Label));
        Assert.Equal(new[] { "ZIP" }, ActionCatalog.For(new[] { _folder }).SelectMany(page => page.Actions).Select(action => action.Label));
    }

    [Fact]
    public void NothingThatDoesntExist_GetsActions() =>
        Assert.Empty(ActionCatalog.For(new[] { Path.Combine(_folder, "gone.png") }));
}
