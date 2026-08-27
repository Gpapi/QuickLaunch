using System;
using System.Runtime.InteropServices;

namespace QuickLaunch.UI.Native;

/// <summary>
/// Win32 entry points the launcher needs. WinUI 3 exposes an HWND for every Window,
/// so anything the WinAppSDK does not surface is reached through here.
/// </summary>
internal static partial class User32
{
    /// <summary>
    /// Returns the DPI for a given window handle. 96 DPI = 100% scale, 120 = 125%, 144 = 150%.
    /// Preferred over XamlRoot.RasterizationScale because the HWND exists as soon as the
    /// window is created — no need to wait for the XAML visual tree to be ready.
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial int GetDpiForWindow(IntPtr hwnd);
}
