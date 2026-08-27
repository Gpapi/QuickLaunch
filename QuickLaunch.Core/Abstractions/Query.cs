namespace QuickLaunch.Core.Abstractions;

/// <summary>
/// A user's query, normalised once so every provider sees the same text.
/// </summary>
/// <param name="Raw">Exactly what is in the search box, including whitespace.</param>
/// <param name="Text">The text providers should match against.</param>
public readonly record struct Query(string Raw, string Text)
{
    public bool IsEmpty => Text.Length == 0;

    /// <summary>
    /// Normalises a raw search box value. Leading and trailing whitespace is dropped;
    /// interior whitespace is left alone, because it is meaningful inside file names
    /// and application titles.
    /// </summary>
    public static Query Parse(string? raw)
    {
        raw ??= string.Empty;
        return new Query(raw, raw.Trim());
    }
}
