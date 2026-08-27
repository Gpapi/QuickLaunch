using System;
using System.Threading;

namespace QuickLaunch.UI.Services;

/// <summary>
/// Ensures one launcher per user session, and lets a second launch summon the first
/// instance instead of starting a rival copy.
/// </summary>
/// <remarks>
/// Uses a named mutex plus a named event rather than the Windows App SDK's
/// <c>AppInstance</c> redirection, because this works identically packaged and unpackaged —
/// the MSIX milestone can adopt it unchanged. Names are session-local (<c>Local\</c>),
/// so separate users on the same machine each get their own launcher.
/// </remarks>
internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationSignal;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    /// <summary>True when this process is the one that owns the launcher.</summary>
    public bool IsFirstInstance { get; }

    public SingleInstanceGate(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.mutex", out bool createdNew);
        IsFirstInstance = createdNew;
        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.activate");
    }

    /// <summary>Asks the already-running instance to show itself. Called from the second process.</summary>
    public void SignalExistingInstance() => _activationSignal.Set();

    /// <summary>
    /// Starts listening for later launches. The callback runs on a thread-pool thread,
    /// so it must marshal to the UI thread itself.
    /// </summary>
    public void ListenForActivation(Action onActivationRequested)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activationSignal,
            (_, _) => onActivationRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _registration?.Unregister(null);
        _registration = null;

        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _activationSignal.Dispose();
    }
}
