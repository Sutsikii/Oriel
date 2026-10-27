using System.Buffers.Binary;
using System.IO.Compression;

namespace Oriel.Platform;

/// <summary>
/// Just enough PNG for the clipboard, which holds bitmaps: writes 8-bit
/// RGBA images, and reads the 8-bit RGB and RGBA ones without interlacing
/// that browsers and most apps write. Others are passed on as PNG only.
/// </summary>
internal static class Png
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] s_crc = CrcTable();

    public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

    /// <summary>A PNG of <paramref name="rgba"/>, 4 bytes a pixel, rows from the top.</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var file = new MemoryStream();
        file.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per sample
        header[9] = 6; // RGBA
        Chunk(file, "IHDR", header);

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0); // no filter
                zlib.Write(rgba.Slice(y * stride, stride));
            }
        }
        Chunk(file, "IDAT", pixels.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>
    /// The pixels of <paramref name="png"/> as RGBA, rows from the top, or
    /// null for a PNG of another kind than 8-bit RGB or RGBA without
    /// interlacing, or a broken one.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba)? Decode(byte[] png)
    {
        try
        {
            return TryDecode(png);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static (int, int, byte[])? TryDecode(byte[] png)
    {
        if (!IsPng(png))
            return null;
        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        var at = Signature.Length;
        while (at + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            at += 12 + length;
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    var bits = data[8];
                    channels = data[9] switch { 2 => 3, 6 => 4, _ => 0 };
                    if (bits != 8 || channels == 0 || data[12] != 0 || width <= 0 || height <= 0 || (long)width * height > 1 << 28)
                        return null;
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    at = png.Length;
                    break;
            }
        }
        if (channels == 0)
            return null;

        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            zlib.ReadExactly(raw);

        var rgba = new byte[width * height * 4];
        var previous = new byte[stride];
        var row = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            raw.AsSpan(y * (stride + 1) + 1, stride).CopyTo(row);
            Unfilter(filter, row, previous, channels);
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                rgba[o] = row[x * channels];
                rgba[o + 1] = row[x * channels + 1];
                rgba[o + 2] = row[x * channels + 2];
                rgba[o + 3] = channels == 4 ? row[x * channels + 3] : (byte)255;
            }
            (previous, row) = (row, previous);
        }
        return (width, height, rgba);
    }

    private static void Unfilter(byte filter, byte[] row, byte[] above, int bpp)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bpp ? row[i - bpp] : 0, up = above[i], corner = i >= bpp ? above[i - bpp] : 0;
            row[i] += filter switch
            {
                0 => 0,
                1 => (byte)left,
                2 => (byte)up,
                3 => (byte)((left + up) / 2),
                4 => (byte)Paeth(left, up, corner),
                _ => throw new InvalidDataException($"PNG filter {filter}"),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream file, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        Span<byte> name = stackalloc byte[4];
        System.Text.Encoding.ASCII.GetBytes(type, name);
        file.Write(name);
        file.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, name), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        file.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = s_crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
