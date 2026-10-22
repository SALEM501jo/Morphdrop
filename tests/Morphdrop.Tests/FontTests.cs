using System.Buffers.Binary;
using System.Windows.Media;
using Morphdrop.Actions;
using Morphdrop.Fonts;

namespace Morphdrop.Tests;

public sealed class FontTests : IDisposable
{
    private static readonly string Arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
    private readonly string _folder = Directory.CreateTempSubdirectory("morphdrop-fonts-").FullName;

    public FontTests()
    {
        File.Copy(Arial, InFolder("arial.ttf"));
    }

    public void Dispose() => TestFolder.Delete(_folder);

    private string InFolder(string name) => Path.Combine(_folder, name);

    /// <summary>Every table identical, except head's checksum field (rewritten for the new file).</summary>
    private static void AssertSameTables(Sfnt expected, Sfnt actual, params string[] except)
    {
        Assert.Equal(expected.Tables.Keys, actual.Tables.Keys);
        foreach ((string tag, byte[] table) in expected.Tables)
        {
            if (except.Contains(tag))
                continue;
            byte[] other = actual.Tables[tag];
            if (tag == "head")
            {
                table.AsSpan(8, 4).Clear();
                other.AsSpan(8, 4).Clear();
            }

            Assert.True(table.AsSpan().SequenceEqual(other), $"the {tag} table changed");
        }
    }

    /// <summary>Loads the font with WPF (Windows' own parser) and compares outlines of many glyphs.</summary>
    private static void AssertSameOutlines(string expectedPath, string actualPath)
    {
        Sta.Run(() =>
        {
            var expected = new GlyphTypeface(new Uri(expectedPath));
            var actual = new GlyphTypeface(new Uri(actualPath));
            Assert.Equal(expected.GlyphCount, actual.GlyphCount);
            for (ushort glyph = 0; glyph < expected.GlyphCount; glyph += 7)
            {
                Geometry a = expected.GetGlyphOutline(glyph, 100, 100), b = actual.GetGlyphOutline(glyph, 100, 100);
                Assert.Equal(a.Bounds, b.Bounds);
                Assert.Equal(a.GetArea(), b.GetArea(), 3);
                Assert.Equal(expected.AdvanceWidths[glyph], actual.AdvanceWidths[glyph]);
            }
        });
    }

    [Fact]
    public void Woff_RoundTrip_KeepsEveryTableByteForByte()
    {
        string woff = FontConverter.Convert(InFolder("arial.ttf"), FontFormat.Woff);
        File.Move(InFolder("arial.ttf"), InFolder("original.ttf"));
        string ttf = FontConverter.Convert(woff, FontFormat.Sfnt);

        Assert.Equal(InFolder("arial.woff"), woff);
        Assert.Equal(InFolder("arial.ttf"), ttf);
        Assert.True(new FileInfo(woff).Length < new FileInfo(ttf).Length * 0.75, "zlib makes it smaller");
        AssertSameTables(FontConverter.Load(InFolder("original.ttf")), FontConverter.Load(ttf));
    }

    [Fact]
    public void Woff2_RoundTrip_KeepsTheOutlines_AndIsMuchSmaller()
    {
        string woff2 = FontConverter.Convert(InFolder("arial.ttf"), FontFormat.Woff2);
        string woff = FontConverter.Convert(InFolder("arial.ttf"), FontFormat.Woff);
        File.Move(InFolder("arial.ttf"), InFolder("original.ttf"));
        string ttf = FontConverter.Convert(woff2, FontFormat.Sfnt);

        Assert.True(new FileInfo(woff2).Length < new FileInfo(woff).Length, "WOFF2 beats WOFF");
        Assert.True(new FileInfo(woff2).Length < new FileInfo(ttf).Length / 2, "WOFF2 at least halves it");
        // Outlines are stored differently, so glyf and loca are compared by what they draw.
        AssertSameTables(FontConverter.Load(InFolder("original.ttf")), FontConverter.Load(ttf), "glyf", "loca");
        AssertSameOutlines(InFolder("original.ttf"), ttf);
    }

    [Fact]
    public void AConvertedFont_HasValidChecksums()
    {
        string woff2 = FontConverter.Convert(InFolder("arial.ttf"), FontFormat.Woff2);
        File.Delete(InFolder("arial.ttf"));
        byte[] ttf = File.ReadAllBytes(FontConverter.Convert(woff2, FontFormat.Sfnt));

        Assert.Equal(0xB1B0AFBAu, Sfnt.Checksum(ttf)); // what head's checkSumAdjustment makes the whole file add up to
    }

    [Fact]
    public void ACffFont_KeepsItsOutlinesAndBecomesOtf()
    {
        var head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5); // magic number
        var maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00005000);
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["CFF "] = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray(),
            ["head"] = head,
            ["maxp"] = maxp,
        };
        File.WriteAllBytes(InFolder("brand.otf"), new Sfnt(Sfnt.OpenTypeCff, tables).Write());

        string woff2 = FontConverter.Convert(InFolder("brand.otf"), FontFormat.Woff2);
        Assert.Contains(ActionCatalog.ForFileName(woff2)[0].Actions, action => action.Label == "OTF");
        Assert.DoesNotContain(ActionCatalog.ForFileName(woff2)[0].Actions, action => action.Label == "TTF");
        File.Move(InFolder("brand.otf"), InFolder("original.otf"));
        string otf = FontConverter.Convert(woff2, FontFormat.Sfnt);

        Assert.Equal(InFolder("brand.otf"), otf);
        AssertSameTables(FontConverter.Load(InFolder("original.otf")), FontConverter.Load(otf));
    }

    [Fact]
    public void FontsGetTheirSlices()
    {
        string woff2 = FontConverter.Convert(InFolder("arial.ttf"), FontFormat.Woff2);

        Assert.Equal(new[] { "WOFF2", "WOFF" }, ActionCatalog.ForFileName(InFolder("arial.ttf"))[0].Actions.Select(action => action.Label));
        Assert.Equal(new[] { "WOFF", "TTF" }, ActionCatalog.ForFileName(woff2)[0].Actions.Select(action => action.Label));
    }

    [Fact]
    public void ADamagedWebFont_SaysSo()
    {
        string woff2 = FontConverter.Convert(InFolder("arial.ttf"), FontFormat.Woff2);
        byte[] bytes = File.ReadAllBytes(woff2);
        File.WriteAllBytes(woff2, bytes[..(bytes.Length / 3)]);

        Assert.ThrowsAny<Exception>(() => FontConverter.Convert(woff2, FontFormat.Sfnt));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(16384u)]
    [InlineData(uint.MaxValue)]
    public void Base128Numbers_RoundTrip(uint value)
    {
        var stream = new MemoryStream();
        Woff2.WriteBase128(stream, value);
        int at = 0;

        Assert.Equal(value, Woff2.ReadBase128(stream.ToArray(), ref at));
    }
}
