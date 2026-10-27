using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Oriel.Platform;
using Oriel.Platform.Windows;

namespace Oriel.Tests;

/// <summary>The formats the clipboard holds, written and read without a clipboard.</summary>
public class ClipboardFormatTests
{
    // 7×5 RGB, as ffmpeg writes it with mixed filters, and its pixels as RGBA.
    private const string FfmpegPng = "iVBORw0KGgoAAAANSUhEUgAAAAcAAAAFCAIAAAAG+GGPAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAPklEQVR4nG2M0QkAIQxDX1xObjy58cRZcrHi3wWaPJpSAWabMpaKW0yu/VWqxq9y5smzY/C65w3rNCGfoOgDhhAbYKqz9pgAAAAASUVORK5CYII=";
    private const string FfmpegPixels = "AAAA//8AAP//AP//AAD/////AP8A/wD/AP///wAAAP8A/////wD//wAA/////wD/AP8A//8AAP8AAAD/AP////8A//8AAP////8A/wD/AP//AAD//wAA///bAP9J/wD/AP+R/wCT//9HAP///wDd/wAAAP8A/////wD//wAA/////wD/AP8A//8AAP8=";

    private static byte[] Pixels(int width, int height) =>
        Enumerable.Range(0, width * height * 4).Select(i => (byte)(i * 37 + i / 4 * 11)).ToArray();

    [Fact]
    public void ReadsThePngsItWrites()
    {
        var rgba = Pixels(9, 4);
        var png = Png.Encode(9, 4, rgba);
        Assert.True(Png.IsPng(png));
        var (width, height, read) = Png.Decode(png)!.Value;
        Assert.Equal((9, 4), (width, height));
        Assert.Equal(rgba, read);
    }

    [Fact]
    public void ReadsThePngsOfOtherEncoders() =>
        Assert.Equal(Convert.FromBase64String(FfmpegPixels), Png.Decode(Convert.FromBase64String(FfmpegPng))!.Value.Rgba);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UndoesEachFilter(byte filter)
    {
        var rgba = Pixels(6, 5);
        var (_, _, read) = Png.Decode(Filtered(6, 5, rgba, filter))!.Value;
        Assert.Equal(rgba, read);
    }

    [Theory]
    [InlineData("not a png")]
    [InlineData("\u0089PNG\r\n\u001a\n")]
    public void ReadsNothingOfOtherData(string data) =>
        Assert.Null(Png.Decode(Encoding.Latin1.GetBytes(data)));

    [Fact]
    public void WritesAndReadsHtmlByItsOffsets()
    {
        var data = WinClipboard.HtmlFormat("<b>bold</b> é ✓");
        Assert.StartsWith("Version:0.9\r\nStartHTML:", Encoding.UTF8.GetString(data));
        Assert.Equal("<b>bold</b> é ✓", WinClipboard.Fragment(data));
    }

    [Fact]
    public void ReadsTheHtmlOfOtherApps()
    {
        // As a browser writes it: offsets of its own, a fragment within a page.
        const string Page = "<html><body><!--StartFragment--><i>x</i><!--EndFragment--></body></html>";
        var header = "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000178\r\nStartFragment:0000000137\r\nEndFragment:0000000145\r\n";
        Assert.Equal(105, header.Length);
        Assert.Equal("<i>x</i>", WinClipboard.Fragment(Encoding.UTF8.GetBytes(header + Page)));
        Assert.Null(WinClipboard.Fragment("Version:0.9\r\n"u8));
    }

    [Fact]
    public void TurnsBitmapsIntoPngsAndBack()
    {
        var rgba = Pixels(5, 3);
        var dib = WinClipboard.Dib(5, 3, rgba);
        Assert.Equal(40 + 5 * 3 * 4, dib.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8))); // bottom-up
        var (_, _, read) = Png.Decode(WinClipboard.DibToPng(dib)!)!.Value;
        Assert.Equal(rgba, read);
    }

    [Fact]
    public void ReadsScreenshotsOf24BitsAndOfNoAlpha()
    {
        // 2×2 at 24 bits, rows padded to 4 bytes, bottom-up: blue, green / red, white.
        var dib = new byte[40 + 8 * 2];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0 }.CopyTo(dib, 40);
        var (_, _, rgba) = Png.Decode(WinClipboard.DibToPng(dib)!)!.Value;
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255, 255, 255, 0, 0, 255, 255, 0, 255, 0, 255 }, rgba);

        // 32 bits of all-zero alpha: opaque.
        var noAlpha = WinClipboard.Dib(1, 1, [10, 20, 30, 0]);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, Png.Decode(WinClipboard.DibToPng(noAlpha)!)!.Value.Rgba);
        Assert.Null(WinClipboard.DibToPng(new byte[12]));
    }

    [Fact]
    public void ListsFilesAsDropFiles()
    {
        var data = WinClipboard.DropFiles([@"C:\a.txt", @"D:\b é.png"]);
        Assert.Equal(20, BinaryPrimitives.ReadInt32LittleEndian(data));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)));
        Assert.Equal("C:\\a.txt\0D:\\b é.png\0\0", Encoding.Unicode.GetString(data, 20, data.Length - 20));
    }

    /// <summary>A PNG of RGBA <paramref name="rgba"/> whose rows all use <paramref name="filter"/>.</summary>
    private static byte[] Filtered(int width, int height, byte[] rgba, byte filter)
    {
        var stride = width * 4;
        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(filter);
                for (var i = 0; i < stride; i++)
                {
                    int x = rgba[y * stride + i], a = i >= 4 ? rgba[y * stride + i - 4] : 0, b = y > 0 ? rgba[(y - 1) * stride + i] : 0;
                    var c = i >= 4 && y > 0 ? rgba[(y - 1) * stride + i - 4] : 0;
                    var predicted = filter switch
                    {
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => 0,
                    };
                    zlib.WriteByte((byte)(x - predicted));
                }
            }
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        using var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", pixels.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        static void Chunk(Stream file, string type, byte[] data)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            file.Write(length);
            file.Write(Encoding.ASCII.GetBytes(type));
            file.Write(data);
            file.Write(new byte[4]); // the reader does not check CRCs
        }
    }
}
