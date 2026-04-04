using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Serializes Roslyn <see cref="ISymbol"/> instances to opaque base-64 handles and resolves
/// them back to symbols in a given <see cref="Solution"/>.
/// </summary>
/// <remarks>
/// A handle encodes the symbol's metadata name, display name, and source location as a
/// base-64-encoded JSON payload. Resolution first attempts lookup by metadata name, then
/// falls back to source position.
/// </remarks>
internal static class SymbolHandleSerializer
{
    /// <summary>
    /// Creates a portable, opaque handle for the given symbol that can be passed back to
    /// <see cref="ResolveHandleAsync"/> to recover the symbol.
    /// </summary>
    /// <param name="symbol">The symbol to serialize.</param>
    /// <returns>A base-64-encoded handle string.</returns>
    public static string CreateHandle(ISymbol symbol)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        var lineSpan = sourceLocation?.GetLineSpan();
        var payload = new SymbolHandlePayload(
            MetadataName: symbol is INamedTypeSymbol namedType ? namedType.ToDisplayString() : null,
            DisplayName: symbol.ToDisplayString(),
            FilePath: lineSpan?.Path,
            Line: lineSpan?.StartLinePosition.Line + 1,
            Column: lineSpan?.StartLinePosition.Character + 1);

        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Attempts to resolve a symbol handle previously created by <see cref="CreateHandle"/> within
    /// the given solution.
    /// </summary>
    /// <param name="solution">The solution to search.</param>
    /// <param name="symbolHandle">The base-64-encoded handle string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved symbol, or <see langword="null"/> if the handle decodes correctly but the symbol cannot be found in the solution.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="symbolHandle"/> is empty, not valid base64, not valid JSON, or not a well-formed handle payload.</exception>
    public static async Task<ISymbol?> ResolveHandleAsync(Solution solution, string symbolHandle, CancellationToken ct)
    {
        var payload = ParseHandlePayload(symbolHandle);

        if (!string.IsNullOrWhiteSpace(payload.MetadataName))
        {
            var symbolByMetadataName = await SymbolResolver.ResolveByMetadataNameAsync(solution, payload.MetadataName, ct).ConfigureAwait(false);
            if (symbolByMetadataName is not null)
            {
                return symbolByMetadataName;
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.FilePath) && payload.Line.HasValue && payload.Column.HasValue)
        {
            var symbolBySource = await SymbolResolver.ResolveAtPositionAsync(
                solution,
                payload.FilePath,
                payload.Line.Value,
                payload.Column.Value,
                ct).ConfigureAwait(false);

            if (symbolBySource is not null)
            {
                if (string.IsNullOrWhiteSpace(payload.DisplayName) ||
                    string.Equals(symbolBySource.ToDisplayString(), payload.DisplayName, StringComparison.Ordinal))
                {
                    return symbolBySource;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a symbol handle string. Throws <see cref="ArgumentException"/> if the string is not a valid handle
    /// (distinguishes malformed handles from "valid handle, symbol not found" at resolution time).
    /// </summary>
    internal static SymbolHandlePayload ParseHandlePayload(string symbolHandle)
    {
        if (string.IsNullOrWhiteSpace(symbolHandle))
        {
            throw new ArgumentException("symbolHandle is empty.", nameof(symbolHandle));
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(symbolHandle.Trim()));
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("symbolHandle is not valid base64.", nameof(symbolHandle), ex);
        }

        SymbolHandlePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SymbolHandlePayload>(json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("symbolHandle is not valid JSON.", nameof(symbolHandle), ex);
        }

        if (payload is null)
        {
            throw new ArgumentException("symbolHandle JSON deserialized to null.", nameof(symbolHandle));
        }

        var hasMetadata = !string.IsNullOrWhiteSpace(payload.MetadataName);
        var hasSource = !string.IsNullOrWhiteSpace(payload.FilePath) && payload.Line.HasValue && payload.Column.HasValue;
        var hasDisplay = !string.IsNullOrWhiteSpace(payload.DisplayName);
        if (!hasMetadata && !hasSource && !hasDisplay)
        {
            throw new ArgumentException(
                "symbolHandle payload must include MetadataName, source location (filePath, line, column), or DisplayName.",
                nameof(symbolHandle));
        }

        return payload;
    }

    internal sealed record SymbolHandlePayload(
        string? MetadataName,
        string? DisplayName,
        string? FilePath,
        int? Line,
        int? Column);
}
