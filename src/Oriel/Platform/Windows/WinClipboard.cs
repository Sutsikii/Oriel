using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Oriel.Platform.Windows;

/// <summary>
/// The Win32 clipboard: CF_UNICODETEXT, "HTML Format", images as "PNG"
/// and CF_DIB (what screenshots put there), and CF_HDROP files. UI thread
/// only; <c>owner</c> is a window of the thread, which owns what it puts there.
/// </summary>
internal static unsafe partial class WinClipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIB = 8;
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint BI_RGB = 0;
    private const uint BI_BITFIELDS = 3;

    // Registered when first used, so that the formats' code runs anywhere (the tests).
    private static uint s_htmlFormat;
    private static uint s_pngFormat;

    private static uint HtmlId => s_htmlFormat != 0 ? s_htmlFormat : s_htmlFormat = RegisterClipboardFormatW("HTML Format");

    private static uint PngId => s_pngFormat != 0 ? s_pngFormat : s_pngFormat = RegisterClipboardFormatW("PNG");

    public static string? GetText(nint owner) => Read(owner, CF_UNICODETEXT, bytes =>
    {
        var text = MemoryMarshal.Cast<byte, char>(bytes);
        var end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
    });

    public static string? GetHtml(nint owner) => Read(owner, HtmlId, bytes => Fragment(bytes));

    public static byte[]? GetImage(nint owner)
    {
        using var open = Open(owner);
        if (IsClipboardFormatAvailable(PngId) && Data(PngId) is { } png && Png.IsPng(png))
            return png;
        return IsClipboardFormatAvailable(CF_DIB) && Data(CF_DIB) is { } dib ? DibToPng(dib) : null;
    }

    public static IReadOnlyList<string>? GetFiles(nint owner)
    {
        using var open = Open(owner);
        var drop = GetClipboardData(CF_HDROP);
        if (drop == 0)
            return null;
        var count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
        var files = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var length = DragQueryFileW(drop, i, null, 0);
            var name = new char[length + 1];
            fixed (char* p = name)
                DragQueryFileW(drop, i, p, length + 1);
            files.Add(new string(name, 0, (int)length));
        }
        return files;
    }

    public static void Set(nint owner, ClipboardContent content)
    {
        using var open = Open(owner);
        if (!EmptyClipboard())
            throw new InvalidOperationException($"The clipboard could not be emptied: error {Marshal.GetLastPInvokeError()}.");
        if (content.Text is { } text)
            Put(CF_UNICODETEXT, MemoryMarshal.AsBytes((text + "\0").AsSpan()));
        if (content.Html is { } html)
            Put(HtmlId, HtmlFormat(html));
        if (content.Png is { } png)
        {
            Put(PngId, png);
            // For the apps that read bitmaps only, when the PNG is of a kind Oriel reads.
            if (Png.Decode(png) is var (width, height, rgba))
                Put(CF_DIB, Dib(width, height, rgba));
        }
        if (content.Files is { } files)
            Put(CF_HDROP, DropFiles(files));
    }

    // ---- Formats ----

    /// <summary>
    /// CF_HTML: a header of UTF-8 offsets, then a page around the fragment
    /// between <c>&lt;!--StartFragment--&gt;</c> and <c>&lt;!--EndFragment--&gt;</c>.
    /// </summary>
    internal static byte[] HtmlFormat(string fragment)
    {
        const string Header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string Before = "<html><body>\r\n<!--StartFragment-->";
        const string After = "<!--EndFragment-->\r\n</body></html>";
        var headerLength = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, Header, 0, 0, 0, 0));
        var body = Encoding.UTF8.GetByteCount(fragment);
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(Before);
        var endFragment = startFragment + body;
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(After);
        var text = string.Format(CultureInfo.InvariantCulture, Header, headerLength, endHtml, startFragment, endFragment) + Before + fragment + After;
        return Encoding.UTF8.GetBytes(text + "\0");
    }

    /// <summary>The fragment of CF_HTML, by its offsets; the whole page when they are missing.</summary>
    internal static string? Fragment(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        if (end >= 0)
            data = data[..end];
        var text = Encoding.UTF8.GetString(data);
        int Offset(string name)
        {
            var at = text.IndexOf(name + ":", StringComparison.Ordinal);
            if (at < 0)
                return -1;
            var digits = text.AsSpan(at + name.Length + 1);
            var stop = digits.IndexOfAny('\r', '\n');
            return int.TryParse(stop < 0 ? digits : digits[..stop], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
        }
        int start = Offset("StartFragment"), stop = Offset("EndFragment");
        if (start < 0 || stop < start || stop > data.Length)
        {
            (start, stop) = (Offset("StartHTML"), Offset("EndHTML"));
            if (start < 0 || stop < start || stop > data.Length)
                return null;
        }
        return Encoding.UTF8.GetString(data[start..stop]);
    }

    /// <summary>A PNG of a CF_DIB of 24 or 32 bits a pixel; null for other DIBs.</summary>
    internal static byte[]? DibToPng(byte[] dib)
    {
        if (dib.Length < 40)
            return null;
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        var height = Math.Abs(rawHeight);
        if (width <= 0 || height <= 0 || (long)width * height > 1 << 28 || bits is not (24 or 32) || compression is not (BI_RGB or BI_BITFIELDS))
            return null;
        // The three masks of BI_BITFIELDS follow a BITMAPINFOHEADER; a V5 header holds them.
        var pixels = headerSize + (compression == BI_BITFIELDS && headerSize == 40 ? 12 : 0);
        var stride = (width * bits / 8 + 3) & ~3;
        if (pixels + (long)stride * height > dib.Length)
            return null;
        var rgba = new byte[width * height * 4];
        // A 32-bit DIB whose alpha is all zero has none: most screenshots.
        var hasAlpha = false;
        for (var y = 0; y < height && bits == 32 && !hasAlpha; y++)
        {
            for (var x = 0; x < width && !hasAlpha; x++)
                hasAlpha = dib[pixels + y * stride + x * 4 + 3] != 0;
        }
        for (var y = 0; y < height; y++)
        {
            var source = pixels + (rawHeight > 0 ? height - 1 - y : y) * stride; // bottom-up unless the height is negative
            for (var x = 0; x < width; x++)
            {
                var s = source + x * bits / 8;
                var o = (y * width + x) * 4;
                rgba[o] = dib[s + 2];
                rgba[o + 1] = dib[s + 1];
                rgba[o + 2] = dib[s];
                rgba[o + 3] = hasAlpha ? dib[s + 3] : (byte)255;
            }
        }
        return Png.Encode(width, height, rgba);
    }

    /// <summary>A CF_DIB of 32 bits a pixel, bottom-up, of RGBA pixels from the top.</summary>
    internal static byte[] Dib(int width, int height, byte[] rgba)
    {
        var dib = new byte[40 + width * height * 4];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20), width * height * 4);
        for (var y = 0; y < height; y++)
        {
            var row = 40 + (height - 1 - y) * width * 4;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                dib[row + x * 4] = rgba[i + 2];
                dib[row + x * 4 + 1] = rgba[i + 1];
                dib[row + x * 4 + 2] = rgba[i];
                dib[row + x * 4 + 3] = rgba[i + 3];
            }
        }
        return dib;
    }

    /// <summary>CF_HDROP: a DROPFILES of wide names, each ended by a null, the list by another.</summary>
    internal static byte[] DropFiles(IReadOnlyList<string> files)
    {
        var names = MemoryMarshal.AsBytes((string.Join('\0', files) + "\0\0").AsSpan());
        var data = new byte[20 + names.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data, 20); // pFiles
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), 1); // fWide
        names.CopyTo(data.AsSpan(20));
        return data;
    }

    // ---- Win32 ----

    /// <summary>Opens the clipboard, which another app may hold for a moment.</summary>
    private static Closer Open(nint owner)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (OpenClipboard(owner))
                return default;
            if (attempt == 20)
                throw new InvalidOperationException($"The clipboard is held by another app: error {Marshal.GetLastPInvokeError()}.");
            Thread.Sleep(10);
        }
    }

    private readonly struct Closer : IDisposable
    {
        public void Dispose() => CloseClipboard();
    }

    private static T? Read<T>(nint owner, uint format, Func<byte[], T?> convert)
        where T : class
    {
        using var open = Open(owner);
        return IsClipboardFormatAvailable(format) && Data(format) is { } data ? convert(data) : null;
    }

    /// <summary>The bytes of <paramref name="format"/>; the clipboard is open.</summary>
    private static byte[]? Data(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == 0)
            return null;
        var memory = (byte*)GlobalLock(handle);
        if (memory is null)
            return null;
        try
        {
            return new ReadOnlySpan<byte>(memory, (int)GlobalSize(handle)).ToArray();
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>Puts <paramref name="bytes"/> as <paramref name="format"/>; the clipboard then owns the memory.</summary>
    private static void Put(uint format, ReadOnlySpan<byte> bytes)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
        if (handle == 0)
            throw new OutOfMemoryException("No memory for the clipboard.");
        var memory = GlobalLock(handle);
        bytes.CopyTo(new Span<byte>((void*)memory, bytes.Length));
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == 0)
        {
            GlobalFree(handle);
            throw new InvalidOperationException($"The clipboard did not take format {format}: error {Marshal.GetLastPInvokeError()}.");
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);
}
