using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Compares a tool call's argument names with the names the tool advertises in its
/// <c>inputSchema.properties</c>. The SDK binder silently drops keys it cannot match, so a
/// typo such as <c>severty</c> would otherwise be lost with no signal. This detector only
/// reports; it never rejects the call (additive <c>_meta.unknownArguments</c> field).
/// </summary>
/// <remarks>
/// Reserved request metadata (<c>_meta</c>, <c>progressToken</c>) travels in <c>params._meta</c>,
/// not in <c>params.arguments</c>, so it never reaches this comparison. Names are compared
/// ordinally because the SDK binder matches argument names ordinally.
/// </remarks>
internal static class UnknownArgumentDetector
{
    private const int MaxSuggestionDistance = 2;

    private static readonly ConditionalWeakTable<McpServerTool, FrozenSet<string>> _declaredNamesCache = new();

    private static readonly ConditionalWeakTable<McpServerTool, string[]> _requiredNamesCache = new();

    /// <summary>
    /// Returns one entry per argument name the matched tool does not declare, or
    /// <see langword="null"/> when every name is declared, the tool cannot be resolved, or its
    /// schema carries no <c>properties</c> object.
    /// </summary>
    internal static IReadOnlyList<UnknownArgumentDto>? Detect(
        RequestContext<CallToolRequestParams> context,
        string toolName)
    {
        var arguments = context.Params?.Arguments;
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        var tool = ResolveTool(context, toolName);
        if (tool is null)
        {
            return null;
        }

        var declared = _declaredNamesCache.GetValue(tool, static t => ReadDeclaredNames(t.ProtocolTool.InputSchema));
        return Detect(arguments.Keys, declared);
    }

    /// <summary>
    /// Pure comparison over argument names and a declared-name set. An empty declared set means
    /// the schema has no <c>properties</c> object, which yields no findings.
    /// </summary>
    internal static IReadOnlyList<UnknownArgumentDto>? Detect(
        IEnumerable<string> argumentNames,
        IReadOnlySet<string> declaredNames)
    {
        if (declaredNames.Count == 0)
        {
            return null;
        }

        List<UnknownArgumentDto>? unknown = null;
        foreach (var name in argumentNames)
        {
            if (declaredNames.Contains(name))
            {
                continue;
            }

            unknown ??= [];
            unknown.Add(new UnknownArgumentDto(name, FindSuggestion(name, declaredNames)));
        }

        return unknown;
    }

    internal static FrozenSet<string> ReadDeclaredNames(JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object ||
            !inputSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return FrozenSet<string>.Empty;
        }

        return properties.EnumerateObject()
            .Select(static property => property.Name)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Finds required schema properties absent from the arguments that will reach the SDK binder.
    /// Schema order is retained so the first omitted property can drive a specific schema hint.
    /// </summary>
    internal static IReadOnlyList<string>? DetectMissingRequiredNames(
        RequestContext<CallToolRequestParams> context,
        string toolName)
    {
        var tool = ResolveTool(context, toolName);
        if (tool is null)
        {
            return null;
        }

        var required = _requiredNamesCache.GetValue(tool, static t => ReadRequiredNames(t.ProtocolTool.InputSchema));
        var argumentNames = context.Params?.Arguments?.Keys;
        var missing = required
            .Where(name => argumentNames is null || !argumentNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        return missing.Length == 0 ? null : missing;
    }

    private static string[] ReadRequiredNames(JsonElement inputSchema)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object ||
            !inputSchema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return required.EnumerateArray()
            .Where(static name => name.ValueKind == JsonValueKind.String)
            .Select(static name => name.GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static McpServerTool? ResolveTool(
        RequestContext<CallToolRequestParams> context,
        string toolName)
    {
        if (context.MatchedPrimitive is McpServerTool matched)
        {
            return matched;
        }

        // MatchedPrimitive may be unset at filter time; fall back to the registered collection
        // the same way internal recovery dispatch resolves a tool by name.
        return context.Services?
            .GetService<IOptions<McpServerOptions>>()?
            .Value
            .ToolCollection?
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ProtocolTool.Name, toolName, StringComparison.Ordinal));
    }

    private static string? FindSuggestion(string name, IReadOnlySet<string> declaredNames)
    {
        string? best = null;
        var bestDistance = MaxSuggestionDistance + 1;
        foreach (var candidate in declaredNames.OrderBy(static n => n, StringComparer.Ordinal))
        {
            var distance = BoundedDistance(name, candidate, MaxSuggestionDistance);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Case-insensitive Levenshtein distance, returning <paramref name="bound"/> + 1 as soon as
    /// the distance is known to exceed <paramref name="bound"/>.
    /// </summary>
    internal static int BoundedDistance(string source, string target, int bound)
    {
        if (Math.Abs(source.Length - target.Length) > bound)
        {
            return bound + 1;
        }

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];
        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];
            var sourceChar = char.ToLowerInvariant(source[i - 1]);
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = sourceChar == char.ToLowerInvariant(target[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }

            if (rowMinimum > bound)
            {
                return bound + 1;
            }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[target.Length], bound + 1);
    }
}
