using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickLaunch.UI.Native;

namespace QuickLaunch.UI.Services;

/// <summary>
/// Raised for every message the <see cref="MessageWindow"/> receives.
/// </summary>
public sealed class WindowMessageEventArgs(uint message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    /// <summary>Set to true to stop the message reaching DefWindowProc.</summary>
    public bool Handled { get; set; }

    /// <summary>The value returned to Windows when <see cref="Handled"/> is true.</summary>
    public IntPtr Result { get; set; }
}

/// <summary>
/// A hidden message-only window that owns the launcher's Win32 message plumbing.
/// </summary>
/// <remarks>
/// Global hot keys and tray icons both need an HWND to deliver messages to. Using a
/// dedicated window rather than subclassing the XAML window keeps that plumbing away
/// from WinUI's own window procedure, and avoids depending on the comctl32 subclassing
/// exports. It must be constructed on the UI thread: messages are delivered by whichever
/// thread's message pump owns the window, and WinUI's pump is the one that is running.
/// </remarks>
internal sealed class MessageWindow : IDisposable
{
    private const string ClassName = "QuickLaunch.MessageWindow";

    // The delegate is passed to native code as a raw function pointer, so a managed
    // reference has to outlive the window or the GC will collect it out from under Windows.
    private readonly Win32.WndProcDelegate _wndProc;
    private readonly IntPtr _hInstance;
    private bool _disposed;

    public IntPtr Handle { get; }

    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public MessageWindow()
    {
        _wndProc = WndProc;
        _hInstance = Win32.GetModuleHandleW(null);

        var windowClass = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = ClassName,
        };

        if (Win32.RegisterClassExW(ref windowClass) == 0)
        {
            const int ErrorClassAlreadyExists = 1410;
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, "Failed to register the launcher message window class.");
            }
        }

        Handle = Win32.CreateWindowExW(
            dwExStyle: 0,
            lpClassName: ClassName,
            lpWindowName: null,
            dwStyle: 0,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: Win32.HWND_MESSAGE,
            hMenu: IntPtr.Zero,
            hInstance: _hInstance,
            lpParam: IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the launcher message window.");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var args = new WindowMessageEventArgs(msg, wParam, lParam);

        try
        {
            MessageReceived?.Invoke(this, args);
        }
        catch
        {
            // An exception must never unwind across the native boundary — that tears down
            // the process with no diagnostics. Swallow it and fall through to DefWindowProc.
        }

        return args.Handled ? args.Result : Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Handle != IntPtr.Zero)
        {
            Win32.DestroyWindow(Handle);
        }

        Win32.UnregisterClassW(ClassName, _hInstance);
    }
}
