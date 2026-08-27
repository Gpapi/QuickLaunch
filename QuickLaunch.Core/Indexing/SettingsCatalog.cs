using System;
using System.Collections.Generic;
using QuickLaunch.Core.Matching;

namespace QuickLaunch.Core.Indexing;

/// <summary>
/// One page of Windows Settings, or a classic Control Panel applet.
/// </summary>
/// <param name="Name">Title as Settings itself shows it.</param>
/// <param name="Target">What the shell is asked to open.</param>
/// <param name="Category">Section the page lives under, shown beneath the title.</param>
/// <param name="Arguments">
/// Command line, for the classic applets that are opened through a host program. These
/// have to stay separate from the target: passed as one string, the shell would look for a
/// program whose name contains the switches.
/// </param>
public sealed record SettingEntry(string Name, string Target, string Category, string? Arguments = null)
{
    public required IReadOnlyList<string> SearchTerms { get; init; }

    public required IReadOnlyList<ulong> SearchTermMasks { get; init; }
}

/// <summary>
/// The Windows Settings pages the launcher can open.
/// </summary>
/// <remarks>
/// Curated rather than discovered: Windows exposes no way to enumerate Settings pages, and
/// the ms-settings URIs are a documented contract instead. The list is deliberately limited
/// to pages worth confirming — an invented URI does not fail until the user presses Enter,
/// which is a worse outcome than the page simply not being listed.
///
/// Aliases carry the words people actually type. Nobody searches for "Display" when their
/// screen is the wrong size; they search for "resolution".
/// </remarks>
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingEntry> Entries { get; } = Build();

    private static IReadOnlyList<SettingEntry> Build()
    {
        (string Name, string Target, string? Arguments, string Category, string[] Aliases)[] pages =
        [
            // ---- System ----
            ("Display", "ms-settings:display", null, "System", ["screen", "resolution", "monitor", "scaling", "brightness"]),
            ("Night light", "ms-settings:nightlight", null, "System", ["blue light", "warm"]),
            ("Sound", "ms-settings:sound", null, "System", ["audio", "speakers", "microphone", "volume"]),
            ("Volume mixer", "ms-settings:apps-volume", null, "System", ["per app volume", "audio mixer"]),
            ("Notifications", "ms-settings:notifications", null, "System", ["alerts", "do not disturb", "focus"]),
            ("Power & battery", "ms-settings:powersleep", null, "System", ["sleep", "screen timeout", "battery"]),
            ("Battery saver", "ms-settings:batterysaver", null, "System", ["power saving"]),
            ("Storage", "ms-settings:storagesense", null, "System", ["disk space", "free up space", "cleanup"]),
            ("Multitasking", "ms-settings:multitasking", null, "System", ["snap", "alt tab", "desktops"]),
            ("Projecting to this PC", "ms-settings:project", null, "System", ["cast", "wireless display"]),
            ("Remote Desktop", "ms-settings:remotedesktop", null, "System", ["rdp"]),
            ("Clipboard", "ms-settings:clipboard", null, "System", ["clipboard history", "paste"]),
            ("About", "ms-settings:about", null, "System", ["device specs", "windows version", "rename pc", "ram"]),
            ("For developers", "ms-settings:developers", null, "System", ["developer mode", "ssh"]),
            ("Troubleshoot", "ms-settings:troubleshoot", null, "System", ["fix problems"]),

            // ---- Bluetooth & devices ----
            ("Bluetooth & devices", "ms-settings:bluetooth", null, "Devices", ["pair", "headphones", "bt"]),
            ("Printers & scanners", "ms-settings:printers", null, "Devices", ["printer", "scanner"]),
            ("Mouse", "ms-settings:mousetouchpad", null, "Devices", ["pointer", "cursor speed"]),
            ("Touchpad", "ms-settings:devices-touchpad", null, "Devices", ["trackpad", "gestures"]),
            ("Pen & Windows Ink", "ms-settings:pen", null, "Devices", ["stylus"]),
            ("AutoPlay", "ms-settings:autoplay", null, "Devices", ["removable drive"]),
            ("USB", "ms-settings:usb", null, "Devices", ["usb notifications"]),

            // ---- Network & internet ----
            ("Network & internet", "ms-settings:network", null, "Network", ["internet", "connection"]),
            ("Wi-Fi", "ms-settings:network-wifi", null, "Network", ["wifi", "wireless", "known networks"]),
            ("Ethernet", "ms-settings:network-ethernet", null, "Network", ["wired", "lan"]),
            ("VPN", "ms-settings:network-vpn", null, "Network", ["vpn connection"]),
            ("Mobile hotspot", "ms-settings:network-mobilehotspot", null, "Network", ["tethering", "share internet"]),
            ("Airplane mode", "ms-settings:network-airplanemode", null, "Network", ["flight mode"]),
            ("Proxy", "ms-settings:network-proxy", null, "Network", ["proxy server"]),
            ("Network & sharing centre", "control.exe", "/name Microsoft.NetworkAndSharingCenter", "Network", ["adapter settings"]),
            ("Network connections", "ncpa.cpl", null, "Network", ["adapters", "network adapter"]),

            // ---- Personalisation ----
            ("Personalisation", "ms-settings:personalization", null, "Personalisation", ["personalization", "appearance"]),
            ("Background", "ms-settings:personalization-background", null, "Personalisation", ["wallpaper", "desktop image"]),
            ("Colours", "ms-settings:colors", null, "Personalisation", ["colors", "accent colour", "dark mode", "light mode"]),
            ("Themes", "ms-settings:themes", null, "Personalisation", ["theme"]),
            ("Lock screen", "ms-settings:lockscreen", null, "Personalisation", ["lockscreen"]),
            ("Start", "ms-settings:personalization-start", null, "Personalisation", ["start menu"]),
            ("Taskbar", "ms-settings:taskbar", null, "Personalisation", ["task bar", "system tray"]),
            ("Fonts", "ms-settings:fonts", null, "Personalisation", ["typeface", "install font"]),

            // ---- Apps ----
            ("Installed apps", "ms-settings:appsfeatures", null, "Apps", ["uninstall", "apps and features", "programs"]),
            ("Default apps", "ms-settings:defaultapps", null, "Apps", ["default browser", "file associations"]),
            ("Optional features", "ms-settings:optionalfeatures", null, "Apps", ["windows features"]),
            ("Startup apps", "ms-settings:startupapps", null, "Apps", ["run at startup", "autostart", "boot"]),
            ("Programs and Features", "appwiz.cpl", null, "Apps", ["uninstall a program", "add remove programs"]),

            // ---- Accounts ----
            ("Your info", "ms-settings:yourinfo", null, "Accounts", ["account", "profile picture"]),
            ("Email & accounts", "ms-settings:emailandaccounts", null, "Accounts", ["mail account"]),
            ("Sign-in options", "ms-settings:signinoptions", null, "Accounts", ["password", "pin", "hello", "fingerprint", "face"]),
            ("Other users", "ms-settings:otherusers", null, "Accounts", ["add user", "family"]),
            ("Windows backup", "ms-settings:backup", null, "Accounts", ["sync settings", "backup"]),

            // ---- Time & language ----
            ("Date & time", "ms-settings:dateandtime", null, "Time & language", ["clock", "timezone", "time zone"]),
            ("Language & region", "ms-settings:regionlanguage", null, "Time & language", ["locale", "keyboard language", "display language"]),
            ("Typing", "ms-settings:typing", null, "Time & language", ["autocorrect", "spell check"]),
            ("Speech", "ms-settings:speech", null, "Time & language", ["voice", "dictation"]),

            // ---- Gaming ----
            ("Game Bar", "ms-settings:gaming-gamebar", null, "Gaming", ["xbox game bar", "recording"]),
            ("Game Mode", "ms-settings:gaming-gamemode", null, "Gaming", ["performance"]),
            ("Captures", "ms-settings:gaming-gamedvr", null, "Gaming", ["game recording", "screenshots"]),

            // ---- Accessibility ----
            ("Accessibility", "ms-settings:easeofaccess", null, "Accessibility", ["ease of access"]),
            ("Text size", "ms-settings:easeofaccess-display", null, "Accessibility", ["bigger text", "make text larger"]),
            ("Magnifier", "ms-settings:easeofaccess-magnifier", null, "Accessibility", ["zoom", "magnify"]),
            ("Narrator", "ms-settings:easeofaccess-narrator", null, "Accessibility", ["screen reader"]),
            ("Contrast themes", "ms-settings:easeofaccess-highcontrast", null, "Accessibility", ["high contrast"]),
            ("Captions", "ms-settings:easeofaccess-closedcaptioning", null, "Accessibility", ["subtitles"]),

            // ---- Privacy & security ----
            ("Privacy & security", "ms-settings:privacy", null, "Privacy & security", ["privacy"]),
            ("Windows Security", "ms-settings:windowsdefender", null, "Privacy & security", ["defender", "antivirus", "virus"]),
            ("Location", "ms-settings:privacy-location", null, "Privacy & security", ["gps"]),
            ("Camera", "ms-settings:privacy-webcam", null, "Privacy & security", ["webcam"]),
            ("Microphone", "ms-settings:privacy-microphone", null, "Privacy & security", ["mic"]),
            ("Windows Firewall", "firewall.cpl", null, "Privacy & security", ["firewall"]),

            // ---- Windows Update ----
            ("Windows Update", "ms-settings:windowsupdate", null, "Windows Update", ["update", "check for updates"]),
            ("Update history", "ms-settings:windowsupdate-history", null, "Windows Update", ["installed updates"]),
            ("Advanced options", "ms-settings:windowsupdate-options", null, "Windows Update", ["active hours", "pause updates"]),
            ("Delivery optimisation", "ms-settings:delivery-optimization", null, "Windows Update", ["delivery optimization"]),
            ("Recovery", "ms-settings:recovery", null, "Windows Update", ["reset this pc", "reinstall windows", "advanced startup"]),
            ("Activation", "ms-settings:activation", null, "Windows Update", ["licence", "license", "product key"]),

            // ---- Classic ----
            ("System Properties", "sysdm.cpl", null, "Control Panel", ["environment variables", "computer name", "remote"]),
            ("Power Options", "powercfg.cpl", null, "Control Panel", ["power plan", "high performance"]),
            ("Internet Options", "inetcpl.cpl", null, "Control Panel", ["internet properties"]),
        ];

        var entries = new List<SettingEntry>(pages.Length);

        foreach (var (name, target, arguments, category, aliases) in pages)
        {
            var terms = new List<string>(aliases.Length + 1) { name };
            terms.AddRange(aliases);

            var masks = new ulong[terms.Count];

            for (int i = 0; i < terms.Count; i++)
            {
                masks[i] = FuzzyMatcher.ComputeMask(terms[i]);
            }

            entries.Add(new SettingEntry(name, target, category, arguments)
            {
                SearchTerms = terms,
                SearchTermMasks = masks,
            });
        }

        return entries;
    }
}
