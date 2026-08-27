using System.Collections.Generic;
using System.Text;

namespace QuickLaunch.UI.Services;

/// <summary>
/// A shortcut the launcher can be summoned with.
/// </summary>
/// <param name="Modifiers">Modifier keys that must be held.</param>
/// <param name="VirtualKey">Windows virtual-key code, e.g. VK_SPACE (0x20).</param>
/// <param name="KeyName">Display name of the non-modifier key, used to build the label.</param>
internal readonly record struct HotKeyBinding(HotKeyModifiers Modifiers, uint VirtualKey, string KeyName)
{
    private const uint VK_SPACE = 0x20;

    /// <summary>
    /// Shortcuts to try at startup, best first.
    /// </summary>
    /// <remarks>
    /// Global hot keys are exclusive session-wide, so the preferred combination is often
    /// already taken — PowerToys Run claims Alt+Space out of the box, for instance. Every
    /// fallback deliberately carries two modifiers: a single-modifier combination such as
    /// Ctrl+Space would be stolen from every other app on the system, breaking things like
    /// editor completion everywhere.
    /// </remarks>
    public static IReadOnlyList<HotKeyBinding> Defaults { get; } =
    [
        new(HotKeyModifiers.Alt, VK_SPACE, "Space"),
        new(HotKeyModifiers.Control | HotKeyModifiers.Alt, VK_SPACE, "Space"),
        new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, VK_SPACE, "Space"),
        new(HotKeyModifiers.Control | HotKeyModifiers.Shift, VK_SPACE, "Space"),
    ];

    /// <summary>Human-readable label, e.g. "Ctrl+Alt+Space".</summary>
    public override string ToString()
    {
        var label = new StringBuilder();

        if (Modifiers.HasFlag(HotKeyModifiers.Control))
        {
            label.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            label.Append("Alt+");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            label.Append("Shift+");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Windows))
        {
            label.Append("Win+");
        }

        return label.Append(KeyName).ToString();
    }
}
