using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Roslyn.Helpers;

internal static class SymbolHandleSerializer
{
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
