using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Creates a <see cref="SymbolLocator"/> from the optional tool parameters supplied by an MCP caller,
/// picking the most specific identification strategy available.
/// </summary>
internal static class SymbolLocatorFactory
{
    /// <summary>
    /// Maximum length of the metadata-name fragment echoed back in not-found error messages
    /// (`symbol-info-not-found-message-locator-vs-location`). Long pasted metadata names — e.g.
    /// the full <c>ToDisplayString()</c> of a generic constructed type — would otherwise leak
    /// kilobytes of caller input into the error envelope.
    /// </summary>
    private const int MetadataNameMessageMaxLength = 200;

    /// <summary>
    /// Maximum length of the symbol-handle fragment echoed back in not-found error messages.
    /// Symbol handles are opaque base64-ish blobs from the resolver; bound the echoed length
    /// for the same payload-bloat reason as <see cref="MetadataNameMessageMaxLength"/>.
    /// </summary>
    private const int SymbolHandleMessageMaxLength = 120;

    /// <summary>
    /// Builds a <see cref="SymbolLocator"/> from the provided parameters.
    /// </summary>
    /// <remarks>
    /// Priority order: <paramref name="symbolHandle"/> (most stable) &gt;
    /// <paramref name="metadataName"/> &gt; <paramref name="filePath"/>/<paramref name="line"/>/<paramref name="column"/>.
    /// All locator-accepting tools advertise <paramref name="metadataName"/> uniformly as of the
    /// top-10 remediation pass (item #10 / `find-references-metadataname-parameter-rejected`); the
    /// previous <c>supportsMetadataName</c> error-tailoring switch is gone because every caller
    /// now supports the parameter.
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// Thrown when none of the identification parameters are supplied, or when a partial source location
    /// is provided (file path / line / column without all three required values, BUG-001).
    /// </exception>
    public static SymbolLocator Create(
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? symbolHandle = null,
        string? metadataName = null)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
            return SymbolLocator.ByHandle(symbolHandle);

        if (!string.IsNullOrWhiteSpace(metadataName))
            return SymbolLocator.ByMetadataName(metadataName);

        var hasFilePath = !string.IsNullOrWhiteSpace(filePath);
        if (hasFilePath && line.HasValue && column.HasValue)
            return SymbolLocator.BySource(filePath!, line.Value, column.Value);

        // BUG-001: When the caller supplied a partial source location (e.g. filePath + line, but no
        // column), the previous behavior was to fall through to the generic "provide one of..." error,
        // which obscured the real problem. Surface the partial-location case explicitly so the caller
        // sees the missing field instead of a vague suggestion.
        if (hasFilePath || line.HasValue || column.HasValue)
        {
            var missing = new List<string>(3);
            if (!hasFilePath) missing.Add("filePath");
            if (!line.HasValue) missing.Add("line");
            if (!column.HasValue) missing.Add("column");
            // FLAG-3A: explicitly disambiguate against the LSP-style "character" name. Some
            // clients borrow LSP Position field names; this server uses "column" everywhere
            // and ignores the LSP-style "character" parameter, so a missing column with an
            // otherwise-complete request often means the caller passed "character" instead.
            var hint = missing.Contains("column")
                ? " Note: this server uses parameter name 'column' (1-based), not the LSP-style 'character'. " +
                  "If your client passed 'character', rename it to 'column'. The column must point at the symbol identifier token (e.g., the interface name), not the start of the line."
                : " Note: column must point at the symbol identifier token (e.g., the interface name), not the start of the line.";
            throw new ArgumentException(
                $"Source location is incomplete. Provide all of filePath, line, and column. Missing: {string.Join(", ", missing)}." + hint);
        }

        throw new ArgumentException(
            "Provide one of: filePath with line and column, symbolHandle, or metadataName.");
    }

    /// <summary>
    /// Formats a "no symbol found" error message that names the locator field the caller
    /// actually supplied — `symbol-info-not-found-message-locator-vs-location`. Pre-fix, every
    /// not-found message read "No symbol found at the specified location" even when the caller
    /// passed only <c>metadataName</c> (no location at all), pointing the caller at a non-existent
    /// position to debug. The branch order matches the most-specific-first contract used by
    /// callers: source location > symbol handle > metadata name. The factory's
    /// <see cref="Create"/> populates exactly one mode, so in practice only one branch fires per
    /// resolved locator — the explicit ordering is defense in depth in case a future caller
    /// constructs a <see cref="SymbolLocator"/> directly with multiple modes set.
    /// </summary>
    /// <remarks>
    /// Long pasted <c>metadataName</c>/<c>symbolHandle</c> values are truncated with an ellipsis
    /// suffix so a multi-kilobyte input doesn't leak verbatim into the error envelope (Risks
    /// field of the planning row).
    /// </remarks>
    public static string FormatSymbolNotFoundMessage(SymbolLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (locator.HasSourceLocation)
        {
            return $"No symbol found at {locator.FilePath}:{locator.Line}:{locator.Column}";
        }

        if (locator.HasHandle)
        {
            var truncatedHandle = TruncateForMessage(locator.SymbolHandle, SymbolHandleMessageMaxLength);
            return $"No symbol found for symbol handle '{truncatedHandle}'";
        }

        if (locator.HasMetadataName)
        {
            var truncatedName = TruncateForMessage(locator.MetadataName, MetadataNameMessageMaxLength);
            return $"No symbol found for metadata name '{truncatedName}'";
        }

        // The factory rejects empty locators upstream; this branch only fires if a future caller
        // constructs a locator with all fields null/empty by hand. Keep the legacy wording so any
        // log or alert still grepping for it on the empty-locator path keeps matching.
        return "No symbol found at the specified location";
    }

    /// <summary>
    /// Trims an echoed locator value to <paramref name="maxLength"/> characters, appending an
    /// ellipsis when truncation occurred. Null/empty inputs are passed through verbatim — callers
    /// guard the relevant <c>Has*</c> property before calling, so an empty value here means the
    /// caller is bypassing the locator predicates and the raw empty string is the most informative
    /// thing to surface.
    /// </summary>
    private static string TruncateForMessage(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (value.Length <= maxLength) return value;
        return value.Substring(0, maxLength) + "...";
    }
}
