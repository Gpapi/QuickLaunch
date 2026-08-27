using System;
using System.Diagnostics;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.Core.Services;

/// <summary>
/// Starts whatever a result points at.
/// </summary>
public static class ResultLauncher
{
    /// <summary>
    /// Activates a target through the shell.
    /// </summary>
    /// <remarks>
    /// UseShellExecute is what makes one code path enough: the shell resolves executables,
    /// documents, folders, URIs and the shell:AppsFolder monikers that start packaged and
    /// unpackaged applications alike. Starting the process directly would handle only the
    /// first of those.
    /// </remarks>
    /// <returns>False if the shell refused, e.g. the target no longer exists.</returns>
    public static bool TryLaunch(LaunchTarget target, out string? error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target.Target,
                Arguments = target.Arguments ?? string.Empty,
                WorkingDirectory = target.WorkingDirectory ?? string.Empty,
                UseShellExecute = true,
            };

            using var process = Process.Start(startInfo);

            error = null;
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or System.IO.FileNotFoundException)
        {
            error = exception.Message;
            return false;
        }
    }
}
