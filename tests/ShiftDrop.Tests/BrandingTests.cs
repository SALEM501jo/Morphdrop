using System.Windows.Media.Imaging;
using ShiftDrop.Branding;
using ShiftDrop.Settings;

namespace ShiftDrop.Tests;

public sealed class BrandingTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("shiftdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    [Fact]
    public void TheLogo_IsDrawnAtEveryIconSize()
    {
        Sta.Run(() => Logo.WriteAssets(_folder));

        using FileStream ico = File.OpenRead(Path.Combine(_folder, "ShiftDrop.ico"));
        var frames = BitmapDecoder.Create(ico, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
        Assert.Equal(Logo.IconSizes, frames.Select(f => f.PixelWidth).Order());
        Assert.Equal(512, TestImages.Read(Path.Combine(_folder, "logo-512.png")).Width);
    }

    [Fact]
    public void TheAppCarriesItsIcon()
    {
        using Stream? icon = typeof(Logo).Assembly.GetManifestResourceStream("ShiftDrop.ico");

        Assert.NotNull(icon);
        Assert.True(icon.Length > 1000);
    }

    [Theory]
    [InlineData("Dark", true)]
    [InlineData("Light", false)]
    public void AnExplicitLook_IgnoresWindowsMode(string choice, bool dark)
    {
        Assert.Equal(dark, AppTheme.Resolve(Enum.Parse<ThemeChoice>(choice)));
    }
}
