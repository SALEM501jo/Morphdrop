using ShiftDrop.Actions;
using static ShiftDrop.Tests.TestImages;

namespace ShiftDrop.Tests;

public sealed class ActionCatalogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AnImage_OffersEveryOtherFormat()
    {
        string png = WritePng(Path.Combine(_folder, "a.png"), 2, 2, (_, _) => Red);

        Assert.Equal(new[] { "JPG", "ICO", "BMP", "TIFF", "GIF" }, ActionCatalog.For(new[] { png }).Select(a => a.Label));
    }

    [Fact]
    public void JpegAliases_CountAsJpg()
    {
        string jpeg = Path.Combine(_folder, "b.JPEG");
        File.WriteAllBytes(jpeg, Array.Empty<byte>());

        Assert.DoesNotContain("JPG", ActionCatalog.For(new[] { jpeg }).Select(a => a.Label));
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
