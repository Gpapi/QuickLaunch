using System;
using System.Runtime.InteropServices;
using QuickLaunch.UI.Native;

namespace QuickLaunch.UI.Services;

/// <summary>
/// The launcher's notification-area icon: the only visible affordance for an app that
/// otherwise lives behind a hot key.
/// </summary>
/// <remarks>
/// Implemented directly on Shell_NotifyIcon rather than through a wrapper package because
/// the message window it needs already exists for hot keys, which makes the dependency
/// unnecessary.
/// </remarks>
internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1;

    private const uint CommandShow = 1;
    private const uint CommandQuit = 2;

    private readonly MessageWindow _messageWindow;

    /// <summary>
    /// Explorer broadcasts this when it restarts; the icon has to be re-added or it is
    /// silently gone for the rest of the session.
    /// </summary>
    private readonly uint _taskbarCreatedMessage;

    private string _tooltip = "QuickLaunch";
    private bool _added;
    private bool _disposed;

    public event EventHandler? ShowRequested;

    public event EventHandler? QuitRequested;

    public TrayIconService(MessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _taskbarCreatedMessage = Win32.RegisterWindowMessageW("TaskbarCreated");
        _messageWindow.MessageReceived += OnMessageReceived;
    }

    public void Show(string? tooltip = null)
    {
        _tooltip = tooltip ?? _tooltip;

        var data = CreateIconData();

        if (Win32.Shell_NotifyIconW(Win32.NIM_ADD, ref data))
        {
            _added = true;
        }
    }

    private Win32.NOTIFYICONDATAW CreateIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATAW>(),
        hWnd = _messageWindow.Handle,
        uID = IconId,
        uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
        uCallbackMessage = Win32.WM_TRAYICON,
        hIcon = Win32.LoadIconW(IntPtr.Zero, Win32.IDI_APPLICATION),
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreatedMessage)
        {
            _added = false;
            Show();
            return;
        }

        if (e.Message != Win32.WM_TRAYICON)
        {
            return;
        }

        // In the classic (pre-version-4) callback contract, lParam carries the mouse message.
        uint mouseMessage = (uint)(e.LParam.ToInt64() & 0xFFFF);

        switch (mouseMessage)
        {
            case Win32.WM_LBUTTONUP:
                e.Handled = true;
                ShowRequested?.Invoke(this, EventArgs.Empty);
                break;

            case Win32.WM_RBUTTONUP:
                e.Handled = true;
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        var menu = Win32.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Win32.AppendMenuW(menu, Win32.MF_STRING, CommandShow, "Show QuickLaunch");
            Win32.AppendMenuW(menu, Win32.MF_STRING | Win32.MF_GRAYED, 0, "Settings…");
            Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenuW(menu, Win32.MF_STRING, CommandQuit, "Quit");

            Win32.GetCursorPos(out var cursor);

            // TrackPopupMenuEx needs its owner in the foreground, otherwise the menu
            // stays up after the user clicks elsewhere.
            Win32.SetForegroundWindow(_messageWindow.Handle);

            int command = Win32.TrackPopupMenuEx(
                menu,
                Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                _messageWindow.Handle,
                IntPtr.Zero);

            switch ((uint)command)
            {
                case CommandShow:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case CommandQuit:
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageWindow.MessageReceived -= OnMessageReceived;

        if (_added)
        {
            var data = new Win32.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATAW>(),
                hWnd = _messageWindow.Handle,
                uID = IconId,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };

            Win32.Shell_NotifyIconW(Win32.NIM_DELETE, ref data);
            _added = false;
        }
    }
}
