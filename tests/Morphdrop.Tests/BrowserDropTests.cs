using System.Text;
using ImageMagick;
using Morphdrop.Overlay;

namespace Morphdrop.Tests;

/// <summary>Browser drags, built the way Chrome, Edge and Firefox hand them to a drop target.</summary>
public sealed class BrowserDropTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-browser-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    /// <summary>A FILEGROUPDESCRIPTORW with one file name, as browsers write it for a dragged picture.</summary>
    private static MemoryStream Descriptor(string name)
    {
        var bytes = new byte[4 + 592];
        BitConverter.GetBytes(1u).CopyTo(bytes, 0);
        Encoding.Unicode.GetBytes(name).CopyTo(bytes, 4 + 72);
        return new MemoryStream(bytes);
    }

    private static byte[] Png()
    {
        using var image = new MagickImage(MagickColors.Orange, 20, 10);
        return image.ToByteArray(MagickFormat.Png);
    }

    [Fact]
    public void ADraggedPicture_IsRecognizedByItsName_AndSavedOnDrop()
    {
        byte[] png = Png();
        string saved = "";
        Sta.Run(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetData("FileGroupDescriptorW", Descriptor("sunset.png"));
            data.SetData("FileContents", new MemoryStream(png));

            BrowserDrop? drop = BrowserDrop.Read(data);

            Assert.NotNull(drop);
            Assert.Equal("sunset.png", drop!.FileName);
            Assert.Null(drop.Link);
            saved = drop.SaveAsync(data, _folder).GetAwaiter().GetResult();
        });

        Assert.Equal(Path.Combine(_folder, "sunset.png"), saved);
        Assert.Equal(png, File.ReadAllBytes(saved));
    }

    [Fact]
    public void ALinkToAFile_IsDownloadedLater()
    {
        Sta.Run(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://example.com/files/Annual%20report.pdf\0")));

            BrowserDrop? drop = BrowserDrop.Read(data);

            Assert.NotNull(drop);
            Assert.Equal("Annual report.pdf", drop!.FileName);
            Assert.Equal("https://example.com/files/Annual%20report.pdf", drop.Link?.OriginalString);
        });
    }

    [Theory]
    [InlineData("https://example.com/about")]
    [InlineData("https://example.com/index.html")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("just some selected text")]
    public void PagesAndPlainText_AreNotFiles(string dragged)
    {
        Sta.Run(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetData("Text", dragged);

            Assert.Null(BrowserDrop.Read(data));
        });
    }

    [Fact]
    public void ANameWithForbiddenCharacters_IsMadeSafe()
    {
        Sta.Run(() =>
        {
            var data = new System.Windows.DataObject();
            data.SetData("FileGroupDescriptorW", Descriptor("a:b?.png"));

            Assert.Equal("a_b_.png", BrowserDrop.Read(data)?.FileName);
        });
    }
}
