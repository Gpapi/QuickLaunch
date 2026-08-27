using System;
using System.Collections.Generic;
using System.Linq;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.UI.ViewModels;

/// <summary>
/// Feeds the results list from a fixed sample set so the design can be built and judged
/// before any real provider exists.
/// </summary>
/// <remarks>
/// TEMPORARY: M2 replaces this with the real search orchestrator. It produces genuine
/// <see cref="SearchResult"/> values and hangs off the same
/// <see cref="MainViewModel.QueryChanged"/> event, so only this class goes away —
/// the view model and views are the finished article.
/// </remarks>
internal sealed class PreviewResultSource
{
    private static readonly SearchResult[] Samples =
    [
        Make("app:code", "Visual Studio Code", "C:\\Users\\...\\Microsoft VS Code", ResultKind.Application),
        Make("app:chrome", "Google Chrome", "C:\\Program Files\\Google\\Chrome", ResultKind.Application),
        Make("app:terminal", "Windows Terminal", "Microsoft.WindowsTerminal", ResultKind.Application),
        Make("app:spotify", "Spotify", "Spotify.exe", ResultKind.Application),
        Make("app:figma", "Figma", "Figma.exe", ResultKind.Application),
        Make("file:report", "Q3 report.docx", "C:\\Users\\papi_\\Documents", ResultKind.File),
        Make("file:budget", "budget-2026.xlsx", "C:\\Users\\papi_\\Documents\\Finance", ResultKind.File),
        Make("file:notes", "meeting-notes.md", "C:\\Users\\papi_\\Desktop", ResultKind.File),
        Make("folder:projects", "GitProjects", "C:\\", ResultKind.Folder),
        Make("folder:downloads", "Downloads", "C:\\Users\\papi_", ResultKind.Folder),
        Make("setting:bluetooth", "Bluetooth & devices", "System settings", ResultKind.Setting),
        Make("setting:display", "Display", "System settings", ResultKind.Setting),
        Make("setting:sound", "Sound", "System settings", ResultKind.Setting),
        Make("url:github", "github.com", "Open in your browser", ResultKind.Url),
    ];

    private static SearchResult Make(string id, string title, string subtitle, ResultKind kind) =>
        new() { Id = id, Title = title, Subtitle = subtitle, Kind = kind };

    /// <summary>Subscribes to a view model so typing produces sample results.</summary>
    public static void Attach(MainViewModel viewModel)
    {
        viewModel.QueryChanged += (_, query) => viewModel.SetResults(Filter(query));
    }

    /// <summary>
    /// Substring matching only. Real ranking is the matcher's job in M2 — approximating
    /// it here would just be a second algorithm to throw away.
    /// </summary>
    private static IEnumerable<SearchResult> Filter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return Samples
            .Where(sample => sample.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Concat(WebFallback(query))
            .Take(20);
    }

    private static IEnumerable<SearchResult> WebFallback(string query)
    {
        yield return Make("web:search", $"Search the web for \u201C{query}\u201D", "Bing", ResultKind.WebSearch);
    }
}
