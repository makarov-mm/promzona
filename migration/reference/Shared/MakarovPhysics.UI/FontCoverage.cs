using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.UI;

/// <summary>Reads a font's cmap, including supplementary CJK: from the selected
/// Windows font through GDI, or from a file on disk for the bundled faces.
/// GDI's UTF-16 GetGlyphIndices API alone cannot establish supplementary coverage.
/// Public so SelfTest can question the shipped files through the same parser.</summary>
public sealed class FontCoverage
{
    private readonly byte[] _data;
    private readonly int[] _tables;

    private FontCoverage(byte[] data, int[] tables) { _data = data; _tables = tables; }

    internal static FontCoverage? Read(Font font, Graphics graphics)
    {
        IntPtr hdc = graphics.GetHdc(), handle = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            handle = font.ToHfont();
            previous = SelectObject(hdc, handle);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return null;
            const uint cmap = 0x70616D63; // GDI tags are packed in little-endian byte order.
            uint size = GetFontData(hdc, cmap, 0, null, 0);
            if (size is 0 or 0xFFFFFFFF || size > 16 * 1024 * 1024) return null;
            var data = new byte[(int)size];
            if (GetFontData(hdc, cmap, 0, data, size) != size) return null;
            return Parse(data);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(hdc, previous);
            if (handle != IntPtr.Zero) DeleteObject(handle);
            graphics.ReleaseHdc(hdc);
        }
    }

    /// <summary>Reads the cmap of a font FILE (TrueType/OpenType, or the first face
    /// of a collection). Bundled fonts live in a GDI+ private collection, which GDI's
    /// GetFontData cannot see, so their coverage comes from the bytes on disk.</summary>
    public static FontCoverage? ReadFile(string path)
    {
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return ParseFile(file);
    }

    internal static FontCoverage? ParseFile(byte[] file)
    {
        if (file.Length < 12) return null;
        int face = 0;
        if (U32(file, 0) == 0x74746366) // 'ttcf': the first face stands for the collection
        {
            if (file.Length < 16 || U32(file, 8) == 0) return null;
            uint offset = U32(file, 12);
            if (offset > (uint)(file.Length - 12)) return null;
            face = (int)offset;
        }
        uint version = U32(file, face);
        if (version is not (0x00010000 or 0x74727565 or 0x4F54544F)) return null; // 1.0, 'true', 'OTTO'
        int count = U16(file, face + 4);
        if (count == 0 || face + 12 + count * 16 > file.Length) return null;
        for (int i = 0; i < count; i++)
        {
            int record = face + 12 + i * 16;
            if (U32(file, record) != 0x636D6170) continue; // 'cmap'
            uint offset = U32(file, record + 8), length = U32(file, record + 12);
            if (offset > (uint)file.Length || length > (uint)file.Length - offset) return null;
            return Parse(file.AsSpan((int)offset, (int)length).ToArray());
        }
        return null;
    }

    internal static FontCoverage? Parse(byte[] data)
    {
        if (data.Length < 4) return null;
        int count = U16(data, 2);
        if (count > (data.Length - 4) / 8) return null;
        var tables = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int record = 4 + i * 8;
            int platform = U16(data, record), encoding = U16(data, record + 2);
            if (platform != 0 && !(platform == 3 && encoding is 1 or 10)) continue;
            uint rawOffset = U32(data, record + 4);
            if (rawOffset > (uint)Math.Max(0, data.Length - 2)) continue;
            int offset = (int)rawOffset, format = U16(data, offset);
            if (format == 4 && offset <= data.Length - 16)
            {
                int length = U16(data, offset + 2), segments = U16(data, offset + 6) / 2;
                if (segments > 0 && length >= 16 + segments * 8 && length <= data.Length - offset)
                    tables.Add(offset);
            }
            else if (format is 12 or 13 && offset <= data.Length - 16)
            {
                uint length = U32(data, offset + 4), groups = U32(data, offset + 12);
                if (length >= 16 && length <= data.Length - offset && groups <= (length - 16) / 12)
                    tables.Add(offset);
            }
        }
        return tables.Count > 0 ? new FontCoverage(data, tables.Distinct().ToArray()) : null;
    }

    /// <summary>Every scalar of the text maps to a real glyph; controls, format
    /// characters and variation selectors are ignored, as the resolver ignores them.</summary>
    public bool Covers(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) continue;
            if (rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF) continue;
            if (!Contains(rune.Value)) return false;
        }
        return true;
    }

    public bool Contains(int scalar)
    {
        if (scalar < 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF) return false;
        foreach (int table in _tables)
        {
            int format = U16(_data, table);
            if (format is 12 or 13)
            {
                int lo = 0, hi = (int)U32(_data, table + 12) - 1;
                while (lo <= hi)
                {
                    int mid = lo + (hi - lo) / 2, at = table + 16 + mid * 12;
                    uint first = U32(_data, at), last = U32(_data, at + 4);
                    if ((uint)scalar < first) hi = mid - 1;
                    else if ((uint)scalar > last) lo = mid + 1;
                    else
                    {
                        uint glyph = U32(_data, at + 8);
                        if (format == 12) glyph += (uint)scalar - first;
                        if (glyph != 0) return true;
                        break;
                    }
                }
            }
            else if (scalar <= 0xFFFF)
            {
                int segments = U16(_data, table + 6) / 2;
                int ends = table + 14, starts = ends + segments * 2 + 2;
                int deltas = starts + segments * 2, offsets = deltas + segments * 2;
                int lo = 0, hi = segments - 1;
                while (lo <= hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    int end = U16(_data, ends + mid * 2), start = U16(_data, starts + mid * 2);
                    if (scalar > end) lo = mid + 1;
                    else if (scalar < start) hi = mid - 1;
                    else
                    {
                        int delta = U16(_data, deltas + mid * 2), offset = U16(_data, offsets + mid * 2);
                        if (offset == 0)
                        {
                            if (((scalar + delta) & 0xFFFF) != 0) return true;
                        }
                        else
                        {
                            int address = offsets + mid * 2 + offset + 2 * (scalar - start);
                            int limit = table + U16(_data, table + 2);
                            if (address >= offsets && address <= limit - 2)
                            {
                                int glyph = U16(_data, address);
                                if (glyph != 0 && ((glyph + delta) & 0xFFFF) != 0) return true;
                            }
                        }
                        break;
                    }
                }
            }
        }
        return false;
    }

    private static ushort U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at, 2));
    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at, 4));

    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern uint GetFontData(IntPtr hdc, uint table, uint offset, [Out] byte[]? data, uint size);
}
