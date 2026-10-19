using Morphdrop.Actions;
using Morphdrop.Imaging;
using Morphdrop.Settings;
using static Morphdrop.Tests.TestImages;

namespace Morphdrop.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-tests-").FullName;

    public void Dispose() => TestFolder.Delete(_folder);

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    [Fact]
    public void WithoutAFile_TheDefaultsApply()
    {
        AppSettings settings = new SettingsStore(SettingsFile).Current;

        Assert.Equal(new AppSettings(), settings);
        Assert.Equal(TriggerKey.Shift, settings.TriggerKey);
        Assert.Equal(new[] { "Convert", "Edit", "Tools" }, settings.EnabledPages.Order());
        Assert.Null(settings.OutputFolder);
    }

    [Fact]
    public void Changes_AreSavedAndReadBack()
    {
        var store = new SettingsStore(SettingsFile);
        AppSettings? announced = null;
        store.Changed += changed => announced = changed;

        store.Update(s => s with { TriggerKey = TriggerKey.Ctrl, Theme = ThemeChoice.Dark, EditPage = false, OutputFolder = @"D:\Results" });

        AppSettings reloaded = new SettingsStore(SettingsFile).Current;
        Assert.Equal(store.Current, reloaded);
        Assert.Equal(store.Current, announced);
        Assert.Equal(TriggerKey.Ctrl, reloaded.TriggerKey);
        Assert.Equal(new[] { "Convert", "Tools" }, reloaded.EnabledPages.Order());
        Assert.Contains("\"Ctrl\"", File.ReadAllText(SettingsFile)); // enums are written as names, readable by people
    }

    [Fact]
    public void AnUnchangedValue_IsNotSavedAgain()
    {
        var store = new SettingsStore(SettingsFile);
        int changes = 0;
        store.Changed += _ => changes++;

        store.Update(s => s with { TriggerKey = TriggerKey.Shift });

        Assert.Equal(0, changes);
        Assert.False(File.Exists(SettingsFile));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"TriggerKey\": \"Space\" }")] // a key Morphdrop doesn't offer
    [InlineData("")]
    public void ADamagedFile_FallsBackToDefaults(string content)
    {
        File.WriteAllText(SettingsFile, content);

        Assert.Equal(new AppSettings(), new SettingsStore(SettingsFile).Current);
    }

    [Fact]
    public void AHandEditedFile_WithCommentsAndUnknownFields_StillLoads()
    {
        File.WriteAllText(SettingsFile, """
            {
              // set by hand
              "Quality": "Smaller",
              "SomethingFromANewerVersion": 42,
            }
            """);

        Assert.Equal(QualityPreset.Smaller, new SettingsStore(SettingsFile).Current.Quality);
    }

    [Fact]
    public void AtLeastOnePage_StaysOn()
    {
        var store = new SettingsStore(SettingsFile);

        store.Update(s => s with { ConvertPage = false, ToolsPage = false, EditPage = false });

        Assert.Equal(3, store.Current.EnabledPages.Count);
    }

    [Theory]
    [InlineData("High", 92u, 85u, 70u)]
    [InlineData("Balanced", 85u, 80u, 60u)]
    [InlineData("Smaller", 75u, 70u, 50u)]
    public void QualityPresets_SetJpegWebPAndAvif(string preset, uint jpeg, uint webp, uint avif)
    {
        Assert.Equal((jpeg, webp, avif), ImageWriter.Qualities(Enum.Parse<QualityPreset>(preset)));
    }

    [Fact]
    public void ResultsGo_NextToTheOriginal_OrIntoTheChosenFolder()
    {
        string input = Path.Combine(_folder, "photos", "a.png");
        string chosen = Path.Combine(_folder, "Morphdrop results");

        Assert.Equal(Path.Combine(_folder, "photos"), OutputFile.DirectoryFor(input, null));
        Assert.Equal(chosen, OutputFile.DirectoryFor(input, chosen));
        Assert.True(Directory.Exists(chosen)); // created when first needed
    }

    [Fact]
    public void TurnedOffPages_LeaveTheWheel()
    {
        string png = WritePng(Path.Combine(_folder, "a.png"), 2, 2, (_, _) => Red);

        IReadOnlyList<ActionPage> pages = ActionCatalog.For(new[] { png }, new HashSet<string> { "Convert", "Edit" });

        Assert.Equal(new[] { "Convert", "Edit" }, pages.Select(p => p.Name));
    }
}
