using System.Text;
using ShiftDrop.Imaging;

namespace ShiftDrop.Tests;

public class FormatDetectorTests
{
    private static byte[] Bytes(params byte[] bytes) => bytes;

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        string brands = major + "\0\0\0\0" + string.Concat(compatible);
        byte[] body = Encoding.ASCII.GetBytes("ftyp" + brands);
        var box = new byte[4 + body.Length];
        box[3] = (byte)box.Length;
        body.CopyTo(box, 4);
        return box;
    }

    public static TheoryData<byte[], string?> Headers => new()
    {
        { Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0), "PNG" },
        { Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10), "JPG" },
        { Encoding.ASCII.GetBytes("GIF89a....."), "GIF" },
        { Encoding.ASCII.GetBytes("RIFF\x10\0\0\0WEBPVP8 "), "WEBP" },
        { Encoding.ASCII.GetBytes("RIFF\x10\0\0\0WAVEfmt "), null }, // a WAV file is RIFF too
        { Ftyp("heic", "mif1", "heic"), "HEIC" },
        { Ftyp("mif1", "mif1", "avif"), "AVIF" },
        { Ftyp("avif", "mif1"), "AVIF" },
        { Ftyp("isom", "iso2", "mp41"), null }, // an MP4 video
        { Encoding.ASCII.GetBytes("II*\0\x08\0\0\0"), "TIFF" },
        { Encoding.ASCII.GetBytes("MM\0*\0\0\0\x08"), "TIFF" },
        { Encoding.ASCII.GetBytes("8BPS\0\x01"), "PSD" },
        { Bytes(0, 0, 1, 0, 1, 0, 16, 16), "ICO" },
        { Encoding.ASCII.GetBytes("BM6\0\0\0"), "BMP" },
        { Encoding.ASCII.GetBytes("<svg xmlns="), null },
        { Encoding.ASCII.GetBytes("push graphic-context"), null }, // ImageMagick's MVG script format
        { Array.Empty<byte>(), null },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void RecognizesFormatsByTheirFirstBytes(byte[] header, string? expected)
    {
        Assert.Equal(expected, FormatDetector.Detect(header)?.Name);
    }
}
