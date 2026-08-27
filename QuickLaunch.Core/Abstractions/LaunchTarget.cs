namespace QuickLaunch.Core.Abstractions;

/// <summary>
/// What to hand to the shell when a result is activated.
/// </summary>
/// <remarks>
/// One shape covers every provider because ShellExecute already understands all of them:
/// an executable path, a document, a folder, an ms-settings: URI, an https: URL, and the
/// shell:AppsFolder\{id} form that launches packaged and unpackaged applications alike.
/// </remarks>
/// <param name="Target">Path, URI or shell moniker to execute.</param>
/// <param name="Arguments">Command line, when the target is an executable.</param>
/// <param name="WorkingDirectory">Directory to start in; the target's own folder if null.</param>
public sealed record LaunchTarget(string Target, string? Arguments = null, string? WorkingDirectory = null);
