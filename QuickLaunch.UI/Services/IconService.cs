using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickLaunch.Core.Abstractions;
using QuickLaunch.UI.Native;

namespace QuickLaunch.UI.Services;

/// <summary>
/// Turns a Core <see cref="IconSource"/> into something the results list can draw.
/// </summary>
/// <remarks>
/// Extraction is a blocking shell call, so it happens on a worker thread; only the final
/// bitmap is built on the UI thread, because XAML objects belong to it. Results are cached
/// because the same handful of applications are re-ranked on every keystroke, and paying
/// the shell again for each of them would put real work between the key and the frame.
///
/// Must be created and awaited on the UI thread.
/// </remarks>
internal sealed class IconService
{
    /// <summary>
    /// Extraction size in physical pixels. Rows draw at 32 DIPs, so this stays sharp up to
    /// 200% scaling and the shell scales down rather than up beyond that.
    /// </summary>
    private const int ExtractionSize = 64;

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImageSource?> GetAsync(IconSource source)
    {
        if (_cache.TryGetValue(source.Path, out var cached))
        {
            return cached;
        }

        ImageSource? image = source.Kind switch
        {
            IconSourceKind.Shell => await LoadFromShellAsync(source.Path),
            IconSourceKind.Image => LoadFromFile(source.Path),
            _ => null,
        };

        // A failed lookup is cached too: whatever made it fail will not change between
        // keystrokes, and retrying would repeat the cost on every search.
        _cache[source.Path] = image;
        return image;
    }

    private static async Task<ImageSource?> LoadFromShellAsync(string parsingName)
    {
        var extracted = await Task.Run(() =>
            ShellIcons.TryGetBitmap(parsingName, ExtractionSize, out byte[] pixels, out int width, out int height)
                ? (pixels, width, height)
                : default);

        if (extracted.pixels is null || extracted.width == 0)
        {
            return null;
        }

        // Back on the UI thread: WriteableBitmap has thread affinity.
        var bitmap = new WriteableBitmap(extracted.width, extracted.height);

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(extracted.pixels);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    private static ImageSource? LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return new BitmapImage(new Uri(path));
    }
}
