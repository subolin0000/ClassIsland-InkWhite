using System.Runtime.InteropServices;
namespace AdaptiveText;
// One reusable bounded DIB for all text in a window. No full-desktop screenshots.
internal sealed class NativeCapture : IDisposable
{
    internal const int MaxWidth = 1024, MaxHeight = 512;
    private const int Stride = MaxWidth * 4;
    private readonly byte[] _pixels = new byte[Stride * MaxHeight];
    private nint _screen, _memory, _bitmap, _old, _bits;
    public NativeCapture()
    {
        try
        {
            _screen = GetDC(0); _memory = CreateCompatibleDC(_screen);
            var info = new BitmapInfo { Size = 40, Width = MaxWidth, Height = -MaxHeight, Planes = 1, BitCount = 32 };
            _bitmap = CreateDIBSection(_screen, ref info, 0, out _bits, 0, 0);
            if (_screen == 0 || _memory == 0 || _bitmap == 0) throw new InvalidOperationException("无法创建屏幕采样缓冲区。");
            _old = SelectObject(_memory, _bitmap); SetStretchBltMode(_memory, 3);
        }
        catch { Dispose(); throw; }
    }
    public Frame? Capture(int x, int y, int width, int height)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77);
        int left = Math.Max(x, vx), top = Math.Max(y, vy);
        int right = (int)Math.Min((long)x + width, (long)vx + GetSystemMetrics(78));
        int bottom = (int)Math.Min((long)y + height, (long)vy + GetSystemMetrics(79));
        if (right <= left || bottom <= top) return null;
        double scale = Math.Min(1, Math.Min((double)MaxWidth / (right - left), (double)MaxHeight / (bottom - top)));
        int w = Math.Max(1, (int)Math.Ceiling((right - left) * scale));
        int h = Math.Max(1, (int)Math.Ceiling((bottom - top) * scale));
        if (!StretchBlt(_memory, 0, 0, w, h, _screen, left, top, right - left, bottom - top, 0x40CC0020)) return null;
        GdiFlush(); Marshal.Copy(_bits, _pixels, 0, Stride * h);
        return new Frame(_pixels, left, top, right - left, bottom - top, w, h);
    }
    // Kept for isolated native regression tests; production captures once per window.
    public byte[]? Sample(int x, int y, int width, int height)
    {
        var frame = Capture(x, y, width, height); if (frame == null) return null;
        var result = new byte[84]; return frame.Sample(x, y, width, height, result) ? result : null;
    }
    internal sealed class Frame(byte[] pixels, int x, int y, int width, int height, int bufferWidth, int bufferHeight)
    {
        // Frame views remain valid until the next Capture call on the same instance.
        public bool Sample(int sx, int sy, int sw, int sh, Span<byte> result)
        {
            if (result.Length < 84) throw new ArgumentException("84-byte sample buffer required.");
            int left = Math.Max(sx, x), top = Math.Max(sy, y);
            int right = (int)Math.Min((long)sx + sw, (long)x + width);
            int bottom = (int)Math.Min((long)sy + sh, (long)y + height);
            if (right <= left || bottom <= top) return false;
            for (int row = 0; row < 3; row++) for (int col = 0; col < 7; col++)
            {
                int px = Math.Clamp((int)((left - x + (col + 0.5) * (right - left) / 7) * bufferWidth / width), 0, bufferWidth - 1);
                int py = Math.Clamp((int)((top - y + (row + 0.5) * (bottom - top) / 3) * bufferHeight / height), 0, bufferHeight - 1);
                pixels.AsSpan(py * Stride + px * 4, 4).CopyTo(result.Slice((row * 7 + col) * 4, 4));
            }
            return true;
        }
    }
    public void Dispose()
    {
        if (_memory != 0 && _old != 0) SelectObject(_memory, _old);
        if (_bitmap != 0) DeleteObject(_bitmap);
        if (_memory != 0) DeleteDC(_memory);
        if (_screen != 0) ReleaseDC(0, _screen);
        _bitmap = _memory = _screen = _old = _bits = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint dest, int dx, int dy, int dw, int dh, nint src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
}
