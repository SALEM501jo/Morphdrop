namespace Morphdrop.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _local = Directory.CreateTempSubdirectory("morphdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_local);

    [Fact]
    public void TheOldShiftDropFolder_IsMovedOverOnce()
    {
        Directory.CreateDirectory(Path.Combine(_local, "ShiftDrop", "ffmpeg"));
        File.WriteAllText(Path.Combine(_local, "ShiftDrop", "settings.json"), "{}");

        string folder = AppPaths.Prepare(_local);

        Assert.Equal(Path.Combine(_local, "Morphdrop"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "settings.json")));
        Assert.True(Directory.Exists(Path.Combine(folder, "ffmpeg")));
        Assert.False(Directory.Exists(Path.Combine(_local, "ShiftDrop")));
    }

    [Fact]
    public void AnExistingMorphdropFolder_IsLeftAlone()
    {
        Directory.CreateDirectory(Path.Combine(_local, "Morphdrop"));
        Directory.CreateDirectory(Path.Combine(_local, "ShiftDrop"));

        Assert.Equal(Path.Combine(_local, "Morphdrop"), AppPaths.Prepare(_local));
        Assert.True(Directory.Exists(Path.Combine(_local, "ShiftDrop")));
    }
}
