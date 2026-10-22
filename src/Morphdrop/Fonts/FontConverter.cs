using Morphdrop.Actions;

namespace Morphdrop.Fonts;

internal enum FontFormat { Sfnt, Woff, Woff2 }

/// <summary>Converts fonts between TTF/OTF (to install) and WOFF/WOFF2 (for websites), without other software.</summary>
internal static class FontConverter
{
    public static FontFormat? FormatOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ttf" or ".otf" => FontFormat.Sfnt,
        ".woff" => FontFormat.Woff,
        ".woff2" => FontFormat.Woff2,
        _ => null,
    };

    /// <summary>The flavor inside a web font (its first 8 bytes), so the wheel can offer TTF or OTF; null for others.</summary>
    public static uint? WebFontFlavor(string path)
    {
        if (FormatOf(path) is not (FontFormat.Woff or FontFormat.Woff2) || !File.Exists(path))
            return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            var header = new byte[8];
            return stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8 ? Sfnt.U32(header, 4) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static Sfnt Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try
        {
            return bytes.Length < 4 ? throw new InvalidDataException("This isn't a font.") : Sfnt.U32(bytes, 0) switch
            {
                Woff.Signature => Woff.Decode(bytes),
                Woff2.Signature => Woff2.Decode(bytes),
                _ => Sfnt.Read(bytes),
            };
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException or EndOfStreamException)
        {
            throw new InvalidDataException("The font file is damaged.", ex);
        }
    }

    /// <summary>Saves the font next to the original in <paramref name="target"/>; a TTF/OTF gets the extension its outlines call for.</summary>
    public static string Convert(string path, FontFormat target)
    {
        Sfnt font = Load(path);
        (byte[] data, string extension) = target switch
        {
            FontFormat.Woff => (Woff.Encode(font), ".woff"),
            FontFormat.Woff2 => (Woff2.Encode(font), ".woff2"),
            _ => (font.Write(), font.Extension),
        };
        return OutputFile.Save(path, null, extension, output => output.Write(data));
    }

    public static IEnumerable<IFileAction> Actions()
    {
        yield return new FileAction("WOFF2", null, "Make a web font (WOFF2)",
            path => FormatOf(path) is FontFormat.Sfnt or FontFormat.Woff, path => Convert(path, FontFormat.Woff2));
        yield return new FileAction("WOFF", null, "Make a web font (WOFF)",
            path => FormatOf(path) is FontFormat.Sfnt or FontFormat.Woff2, path => Convert(path, FontFormat.Woff));
        yield return new FileAction("TTF", null, "Make a font to install (TTF)",
            path => WebFontFlavor(path) is { } flavor && flavor != Sfnt.OpenTypeCff, path => Convert(path, FontFormat.Sfnt));
        yield return new FileAction("OTF", null, "Make a font to install (OTF)",
            path => WebFontFlavor(path) == Sfnt.OpenTypeCff, path => Convert(path, FontFormat.Sfnt));
    }
}
