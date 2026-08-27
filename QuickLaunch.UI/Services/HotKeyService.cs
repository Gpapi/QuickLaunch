using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using QuickLaunch.UI.Native;

namespace QuickLaunch.UI.Services;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>
/// Registers system-wide hot keys against the launcher's message window.
/// </summary>
internal sealed class HotKeyService : IDisposable
{
    /// <summary>
    /// Suppresses the auto-repeat storm you would otherwise get from holding the combo down.
    /// Always OR'd into the modifiers.
    /// </summary>
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly MessageWindow _messageWindow;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;
    private bool _disposed;

    public HotKeyService(MessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _messageWindow.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Attempts to claim a hot key. Hot keys are exclusive process-wide across the whole
    /// session, so this genuinely fails when another app already owns the combination —
    /// callers must surface that rather than assume success.
    /// </summary>
    /// <param name="virtualKey">A Windows virtual-key code, e.g. VK_SPACE (0x20).</param>
    public bool TryRegister(HotKeyModifiers modifiers, uint virtualKey, Action onPressed, out string? error)
    {
        int id = _nextId++;

        if (!Win32.RegisterHotKey(_messageWindow.Handle, id, (uint)modifiers | MOD_NOREPEAT, virtualKey))
        {
            _nextId--;
            int code = Marshal.GetLastWin32Error();

            const int ErrorHotKeyAlreadyRegistered = 1409;
            error = code == ErrorHotKeyAlreadyRegistered
                ? "That shortcut is already in use by another application."
                : $"Windows refused the shortcut (error {code}).";

            return false;
        }

        _handlers[id] = onPressed;
        error = null;
        return true;
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message != Win32.WM_HOTKEY)
        {
            return;
        }

        if (_handlers.TryGetValue((int)e.WParam, out var handler))
        {
            e.Handled = true;
            handler();
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

        foreach (int id in _handlers.Keys)
        {
            Win32.UnregisterHotKey(_messageWindow.Handle, id);
        }

        _handlers.Clear();
    }
}
