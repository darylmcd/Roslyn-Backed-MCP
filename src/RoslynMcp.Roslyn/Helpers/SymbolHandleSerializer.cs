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
    /// <returns>The resolved symbol, or <see langword="null"/> if the handle is malformed or the symbol cannot be found.</returns>
    public static async Task<ISymbol?> ResolveHandleAsync(Solution solution, string symbolHandle, CancellationToken ct)
    {
        SymbolHandlePayload? payload;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(symbolHandle));
            payload = JsonSerializer.Deserialize<SymbolHandlePayload>(json);
        }
        catch (Exception)
        {
            return null;
        }

        if (payload is null)
        {
            return null;
        }

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

    private sealed record SymbolHandlePayload(
        string? MetadataName,
        string? DisplayName,
        string? FilePath,
        int? Line,
        int? Column);
}
