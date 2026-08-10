using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Contracts;

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
public static class SymbolHandleSerializer
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

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: builds a short, descriptive label that
    /// disambiguates one candidate from N siblings in a select-from-N elicitation prompt
    /// (or the additive disambiguation-list response when elicitation is unavailable).
    /// Format: <c>"&lt;Kind&gt; &lt;DisplayString&gt; — &lt;file&gt;:&lt;line&gt;"</c> when a source location
    /// exists; the file path is normalized to <c>Path.GetFileName</c> so we never leak
    /// absolute paths from internal-visibility code into the prompt the user sees.
    /// </summary>
    /// <remarks>
    /// Risks call-out from the initiative: "labels must produce labels that disambiguate
    /// candidates without leaking sensitive type names from internal-visibility code." The
    /// type-and-member display string is the public-API surface (Roslyn already strips
    /// internal type members from its <c>ToDisplayString</c> when configured); the file path
    /// is the only locally identifiable string we emit, and we trim it to a basename.
    /// </remarks>
    public static string BuildDisplayLabel(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var kind = symbol.Kind.ToString();
        var display = symbol.ToDisplayString();
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null)
        {
            return $"{kind} {display}";
        }

        var lineSpan = loc.GetLineSpan();
        var fileBase = string.IsNullOrEmpty(lineSpan.Path)
            ? null
            : Path.GetFileName(lineSpan.Path);
        var lineNumber = lineSpan.StartLinePosition.Line + 1;
        return string.IsNullOrEmpty(fileBase)
            ? $"{kind} {display}"
            : $"{kind} {display} — {fileBase}:{lineNumber}";
    }

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: returns ALL symbols across the solution
    /// that match the given <paramref name="metadataName"/> — types via
    /// <see cref="Compilation.GetTypeByMetadataName"/> AND members via the trailing-dot split
    /// (each <c>GetMembers(name)</c> overload contributes every overload). Results are
    /// deduped by the stable <c>symbolHandle</c> clients re-submit so the same symbol seen
    /// through multiple <c>Compilation</c>s or assemblies does not produce an ambiguity the
    /// caller cannot resolve.
    /// </summary>
    /// <remarks>
    /// This is the multi-result counterpart of
    /// <see cref="SymbolResolver.ResolveByMetadataNameAsync"/>. Callers who need exactly one
    /// symbol should keep using the single-result variant; callers that want to detect
    /// ambiguity (overload set, partial-class siblings, member-vs-type collisions) ahead of
    /// elicitation should call this and inspect <c>Count &gt; 1</c>.
    /// </remarks>
    /// <param name="compilationCache">
    /// compilation-cache-adoption-read-side: optional shared compilation cache. When supplied together
    /// with <paramref name="workspaceId"/> AND <paramref name="solution"/> is the live workspace
    /// solution, the per-project compilation fetch is routed through it. Omitted (the default) or a
    /// forked solution takes the raw fetch — see
    /// <see cref="SymbolResolver.CanUseCompilationCache"/>.
    /// </param>
    /// <param name="workspaceId">The workspace session identifier, required to key the cache.</param>
    public static async Task<IReadOnlyList<ISymbol>> FindAllByMetadataNameAsync(
        Solution solution,
        string metadataName,
        CancellationToken ct,
        ICompilationCache? compilationCache = null,
        string? workspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        if (string.IsNullOrWhiteSpace(metadataName)) return Array.Empty<ISymbol>();

        var matches = new List<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var useCache = SymbolResolver.CanUseCompilationCache(solution, compilationCache, workspaceId);

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = useCache
                ? await compilationCache!.GetCompilationAsync(workspaceId!, project, ct).ConfigureAwait(false)
                : await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Type-by-full-name fast path — collect, do not return early.
            var typeSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (typeSymbol is not null)
            {
                AddCandidateIfNew(matches, seen, typeSymbol);
            }

            // Member fallback: split at the LAST dot. All overloads of the named member
            // contribute to the candidate set — that's exactly the disambiguation case the
            // initiative targets (e.g. method overloads of a single name).
            var lastDot = metadataName.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == metadataName.Length - 1) continue;

            var containingTypeName = metadataName[..lastDot];
            var memberName = metadataName[(lastDot + 1)..];
            var containingType = compilation.GetTypeByMetadataName(containingTypeName);
            if (containingType is null) continue;

            foreach (var member in containingType.GetMembers(memberName))
            {
                AddCandidateIfNew(matches, seen, member);
            }
        }

        return matches;
    }

    private static void AddCandidateIfNew(List<ISymbol> matches, HashSet<string> seen, ISymbol symbol)
    {
        if (seen.Add(CreateHandle(symbol)))
        {
            matches.Add(symbol);
        }
    }
}
