using System;
using System.Runtime.InteropServices;

namespace QuickLaunch.UI.Native;

/// <summary>
/// Extracts the icon the shell would show for a file, folder or application.
/// </summary>
/// <remarks>
/// Uses IShellItemImageFactory rather than SHGetFileInfo because it returns a real 32-bit
/// image at whatever size is asked for — including the high-resolution artwork packaged
/// apps ship — where SHGetFileInfo is limited to the small and large system icon sizes.
/// </remarks>
internal static class ShellIcons
{
    private static Guid _imageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>Icon rather than a document thumbnail, scaled up if the source is smaller.</summary>
    private const uint SIIGBF_ICONONLY = 0x00000004;
    private const uint SIIGBF_SCALEUP = 0x00000100;

    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    /// <summary>
    /// Renders an icon into straight BGRA bytes, top row first.
    /// </summary>
    /// <returns>False when the shell has no image for this item.</returns>
    internal static bool TryGetBitmap(string parsingName, int size, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        IntPtr bitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;

        try
        {
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref _imageFactoryId, out factory) != 0
                || factory is null)
            {
                return false;
            }

            if (factory.GetImage(new SIZE(size, size), SIIGBF_ICONONLY | SIIGBF_SCALEUP, out bitmap) != 0
                || bitmap == IntPtr.Zero)
            {
                return false;
            }

            return TryCopyPixels(bitmap, out pixels, out width, out height);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    private static bool TryCopyPixels(IntPtr bitmap, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        var info = new BITMAP();

        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), ref info) == 0 || info.bmWidth <= 0 || info.bmHeight <= 0)
        {
            return false;
        }

        width = info.bmWidth;
        height = info.bmHeight;

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,

            // Negative height asks GDI for a top-down image, which is the order every
            // imaging API here expects. Left positive it would arrive upside down.
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        var buffer = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);

        try
        {
            if (GetDIBits(screen, bitmap, 0, (uint)height, buffer, ref header, DIB_RGB_COLORS) == 0)
            {
                return false;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        pixels = buffer;
        return true;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, ref BITMAP output);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}
