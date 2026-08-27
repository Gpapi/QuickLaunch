using System;
using System.IO;

namespace QuickLaunch.UI.Services;

/// <summary>
/// Last-resort record of an unhandled exception.
/// </summary>
/// <remarks>
/// A launcher spends its life with no console and usually no visible window, so a crash
/// otherwise leaves nothing behind but a stowed-exception exit code. Writing under
/// %LOCALAPPDATA%\QuickLaunch keeps this working unchanged once the app is packaged.
/// </remarks>
internal static class CrashLog
{
    private const long MaxBytes = 256 * 1024;

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickLaunch",
        "crash.log");

    public static void Write(Exception exception)
    {
        try
        {
            string path = Path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            // Never let the log grow without bound on a repeating fault.
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                File.Delete(path);
            }

            File.AppendAllText(path, $"=== {DateTimeOffset.Now:u} ==={Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Reporting a crash must never cause one.
        }
    }
}
