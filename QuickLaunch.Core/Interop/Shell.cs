using System;
using System.Runtime.InteropServices;

namespace QuickLaunch.Core.Interop;

/// <summary>
/// The slice of the Windows shell needed to enumerate installed applications.
/// </summary>
internal static class Shell
{
    /// <summary>
    /// The Applications virtual folder: every app Windows itself will show you, packaged
    /// and unpackaged together, already de-duplicated and named the way the Start menu
    /// names them.
    /// </summary>
    internal static Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    internal static Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    internal static Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    internal static Guid IidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");

    /// <summary>Names a shell item can be asked for.</summary>
    internal static class Sigdn
    {
        /// <summary>What the user sees, e.g. "Visual Studio Code".</summary>
        internal const uint NormalDisplay = 0x00000000;

        /// <summary>
        /// Within the Applications folder this is the launch identity: an AppUserModelId
        /// for packaged apps, and a shortcut identifier for unpackaged ones.
        /// </summary>
        internal const uint ParentRelativeParsing = 0x80018001;

        /// <summary>A real path on disk. Fails for virtual items such as packaged apps.</summary>
        internal const uint FileSysPath = 0x80058000;
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint celt, out IShellItem rgelt, out uint pceltFetched);

        [PreserveSig]
        int Skip(uint celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone(out IEnumShellItems ppenum);
    }

    [DllImport("shell32.dll", ExactSpelling = true, PreserveSig = true)]
    internal static extern int SHGetKnownFolderItem(
        ref Guid rfid,
        uint flags,
        IntPtr hToken,
        ref Guid riid,
        out IShellItem ppv);

    /// <summary>
    /// Reads one of a shell item's names, or null when the item has no name of that kind.
    /// The shell allocates the string, so it has to be freed here.
    /// </summary>
    internal static string? GetDisplayName(IShellItem item, uint kind)
    {
        if (item.GetDisplayName(kind, out IntPtr buffer) != 0 || buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }
}
