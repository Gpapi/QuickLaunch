using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using QuickLaunch.UI.Services;

namespace QuickLaunch.UI;

/// <summary>
/// Hand-written entry point (the generated one is suppressed by DISABLE_XAML_GENERATED_MAIN).
/// Owning Main is what lets the single-instance check run before any XAML is created, so a
/// duplicate launch costs almost nothing and never flashes a window.
/// </summary>
public static class Program
{
    /// <summary>Started by the boot task, so the window stays hidden until summoned.</summary>
    private const string BackgroundSwitch = "--background";

    /// <summary>
    /// Keeps the launcher on screen when it loses focus. Only useful while developing —
    /// it is what makes the window survive long enough to be inspected or screenshotted.
    /// </summary>
    private const string NoAutoHideSwitch = "--no-auto-hide";

    [STAThread]
    private static void Main(string[] args)
    {
        using var gate = new SingleInstanceGate("QuickLaunch");

        if (!gate.IsFirstInstance)
        {
            // Someone launched us again — treat that as "show the launcher" and get out.
            gate.SignalExistingInstance();
            return;
        }

        bool startHidden = HasSwitch(args, BackgroundSwitch);
        bool autoHide = !HasSwitch(args, NoAutoHideSwitch);

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(initializationParams =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcherQueue));

            _ = new App(gate, startHidden, autoHide);
        });
    }

    private static bool HasSwitch(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
