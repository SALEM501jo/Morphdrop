using System.Formats.Tar;
using System.IO.Compression;
using Morphdrop.Actions;
using Morphdrop.Archives;

namespace Morphdrop.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-archives-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    private string Text(string relative, string content = "hello")
    {
        string path = InFolder(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string[] EntriesOf(string zip)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(entry => entry.FullName).Order().ToArray();
    }

    [Fact]
    public void OneFile_BecomesAZipNamedAfterIt()
    {
        string notes = Text("notes.txt");

        string zip = Archives.Archives.Zip(new[] { notes });

        Assert.Equal(InFolder("notes.zip"), zip);
        Assert.Equal(new[] { "notes.txt" }, EntriesOf(zip));
    }

    [Fact]
    public void AFolder_KeepsItsNameInsideTheZip()
    {
        Text("Photos/a.txt");
        Text("Photos/trip/b.txt");
        Directory.CreateDirectory(InFolder("Photos/empty"));

        string zip = Archives.Archives.Zip(new[] { InFolder("Photos") });

        Assert.Equal(InFolder("Photos.zip"), zip);
        Assert.Equal(new[] { "Photos/a.txt", "Photos/empty/", "Photos/trip/b.txt" }, EntriesOf(zip));
    }

    [Fact]
    public void SeveralItems_GoIntoOneZip()
    {
        string a = Text("a.txt");
        string b = Text("b.txt");

        string zip = ArchiveAction.Zip("x").RunAll(new[] { a, b });

        Assert.Equal(InFolder("a (2 items).zip"), zip);
        Assert.Equal(new[] { "a.txt", "b.txt" }, EntriesOf(zip));
    }

    [Fact]
    public void AZip_IsExtractedIntoAFolderNamedAfterIt()
    {
        string zip = InFolder("backup.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("docs/readme.txt").Open()))
                writer.Write("read me");
            archive.CreateEntry("empty/");
        }

        string folder = Archives.Archives.Extract(zip);

        Assert.Equal(InFolder("backup"), folder);
        Assert.Equal("read me", File.ReadAllText(Path.Combine(folder, "docs", "readme.txt")));
        Assert.True(Directory.Exists(Path.Combine(folder, "empty")));
    }

    [Fact]
    public void AZipHoldingOneFolderOfTheSameName_IsNotNestedTwice()
    {
        string zip = InFolder("Photos.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("Photos/a.txt").Open()))
            writer.Write("a");

        string folder = Archives.Archives.Extract(zip);

        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public void ATarGz_IsExtracted()
    {
        string source = Text("src/hello.txt", "tar");
        string tgz = InFolder("site.tar.gz");
        using (FileStream file = File.Create(tgz))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
            tar.WriteEntry(source, "hello.txt");

        string folder = Archives.Archives.Extract(tgz);

        Assert.Equal(InFolder("site"), folder);
        Assert.Equal("tar", File.ReadAllText(Path.Combine(folder, "hello.txt")));
    }

    [Fact]
    public void AnEntryClimbingOutOfTheFolder_StopsTheExtraction()
    {
        string zip = InFolder("evil.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()))
            writer.Write("gotcha");

        Assert.Throws<InvalidDataException>(() => Archives.Archives.Extract(zip));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_folder)!, "outside.txt")));
        Assert.False(Directory.Exists(InFolder("evil")), "the half-made folder is removed");
    }

    [Fact]
    public void AFolder_GetsAZipSlice_AndAnArchiveAnExtractSlice()
    {
        Directory.CreateDirectory(InFolder("Projects"));
        string zip = Archives.Archives.Zip(new[] { Text("x.txt") });

        IReadOnlyList<ActionPage> forFolder = ActionCatalog.For(new[] { InFolder("Projects") });
        IReadOnlyList<ActionPage> forZip = ActionCatalog.For(new[] { zip });

        Assert.Equal(new[] { "ZIP" }, forFolder.SelectMany(page => page.Actions).Select(action => action.Label));
        Assert.Equal(new[] { "Extract" }, forZip.SelectMany(page => page.Actions).Select(action => action.Label));
    }

    [Fact]
    public void AnyFile_CanBeZipped()
    {
        IReadOnlyList<ActionPage> pages = ActionCatalog.ForFileName(InFolder("setup.unknownext"));

        Assert.Equal("ZIP", Assert.Single(Assert.Single(pages).Actions).Label);
    }
}
