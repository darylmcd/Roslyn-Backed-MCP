using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// inv-arg-envelope-schema-hint: cached reflection over every
/// <see cref="McpServerToolAttribute"/>-attributed method in the Host.Stdio assembly,
/// projecting each tool's user-facing parameters into a name → schema lookup. A parameter is
/// user-facing when the MCP SDK binds it from the request body (see <c>IsUserFacing</c>),
/// regardless of whether it carries a <see cref="DescriptionAttribute"/>.
/// <para>
/// <see cref="ToolErrorHandler"/> consults this index to attach a per-parameter
/// <c>schemaHint</c> field to <c>InvalidArgument</c> envelopes so cold-context callers
/// can re-call without round-tripping through <c>server_info</c>. Reflection runs once
/// at first access; the dictionary is immutable thereafter.
/// </para>
/// </summary>
internal static class ToolParameterIndex
{
    private static readonly Lazy<FrozenDictionary<string, FrozenDictionary<string, ToolParameterSchema>>> s_index =
        new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns the cached parameter schema for <paramref name="toolName"/>'s
    /// <paramref name="parameterName"/>, or <see langword="null"/> when no match exists.
    /// Both lookups are case-sensitive (parameter names use the original C# casing —
    /// the SDK already routes JSON camelCase to PascalCase before binding throws).
    /// </summary>
    public static ToolParameterSchema? GetParameter(string toolName, string parameterName)
    {
        if (string.IsNullOrEmpty(toolName) || string.IsNullOrEmpty(parameterName)) return null;
        return s_index.Value.TryGetValue(toolName, out var parameters)
               && parameters.TryGetValue(parameterName, out var schema)
            ? schema
            : null;
    }

    /// <summary>
    /// Returns all known parameters for <paramref name="toolName"/>, or an empty list when
    /// the tool is unknown. Used to format a tool-level schema hint when the failing
    /// parameter could not be identified (e.g. a JSON deserialization error before binding).
    /// </summary>
    public static IReadOnlyCollection<ToolParameterSchema> GetParameters(string toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return Array.Empty<ToolParameterSchema>();
        return s_index.Value.TryGetValue(toolName, out var parameters)
            ? parameters.Values
            : Array.Empty<ToolParameterSchema>();
    }

    private static FrozenDictionary<string, FrozenDictionary<string, ToolParameterSchema>> BuildIndex()
    {
        // Anchor on a known tool-host type so we walk the same assembly that MCP discovery uses.
        var assembly = typeof(Tools.AnalysisTools).Assembly;
        var dict = new Dictionary<string, FrozenDictionary<string, ToolParameterSchema>>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            // Tools are public static methods on [McpServerToolType] classes per SDK convention,
            // but include NonPublic/Instance for forward-compatibility with future shapes.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Static | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr?.Name is null) continue;

                var parameters = method.GetParameters()
                    .Where(IsUserFacing)
                    .Select(BuildSchema)
                    .ToDictionary(p => p.Name, StringComparer.Ordinal);

                if (parameters.Count > 0)
                {
                    dict[attr.Name] = parameters.ToFrozenDictionary(StringComparer.Ordinal);
                }
            }
        }

        return dict.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// A parameter is user-facing when the MCP SDK advertises it in the tool's input schema and
    /// binds it from the JSON request body. The SDK excludes only DI/framework-supplied
    /// parameters: <see cref="CancellationToken"/>, <see cref="McpServer"/>,
    /// <see cref="RequestContext{TParams}"/>, and services resolved from the container
    /// (<see cref="PromptParameterClassifier.IsServiceType"/> — interfaces such as
    /// <see cref="IProgress{T}"/> and Microsoft.Extensions types). Every other parameter is
    /// caller input whether or not it carries a <see cref="DescriptionAttribute"/>; selecting on
    /// the attribute hid undescribed bound parameters from every index consumer. DI membership is
    /// only knowable from the container, so <c>ToolInputSchemaHygieneTests</c> asserts
    /// index/schema parity across the whole surface.
    /// </summary>
    internal static bool IsUserFacing(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type == typeof(CancellationToken) || type == typeof(McpServer)) return false;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RequestContext<>)) return false;
        return !IsHostSupplied(type);
    }

    /// <summary>
    /// Concrete (non-interface) services the SDK resolves from the container. They carry no
    /// structural marker, so they are enumerated; the whole-surface parity test fails loudly
    /// when a new one appears on a tool method.
    /// </summary>
    private static readonly FrozenSet<Type> s_hostSuppliedConcreteTypes =
        new[] { typeof(ServerProcessMetadata), typeof(ValidationServiceOptions) }.ToFrozenSet();

    private static bool IsHostSupplied(Type type)
    {
        if (s_hostSuppliedConcreteTypes.Contains(type)) return true;

        // Collection interfaces (IReadOnlyList<string>, IEnumerable<T>, ...) are JSON-bound caller
        // input, not container services, even though PromptParameterClassifier treats every
        // interface as a service.
        if (type.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true) return false;

        return PromptParameterClassifier.IsServiceType(type);
    }

    private static ToolParameterSchema BuildSchema(ParameterInfo parameter)
    {
        var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var typeName = CatalogTypeNameFormatter.FormatTypeName(parameter.ParameterType);
        var required = !parameter.HasDefaultValue;

        return new ToolParameterSchema(
            Name: parameter.Name ?? string.Empty,
            Type: typeName,
            Required: required,
            Description: description);
    }
}

/// <summary>
/// Per-parameter schema row served to <see cref="ToolErrorHandler"/> for inclusion in
/// <c>InvalidArgument</c> envelopes. Mirrors <see cref="PromptParameterEntry"/> minus the
/// default-value field — error envelopes care about the contract, not the runtime fallback.
/// </summary>
/// <param name="Name">The parameter name as declared on the C# tool method.</param>
/// <param name="Type">A C#-style type label (e.g. <c>string</c>, <c>int?</c>, <c>List&lt;string&gt;</c>).</param>
/// <param name="Required">When <see langword="true"/>, the parameter has no default and MUST be supplied.</param>
/// <param name="Description">The <see cref="DescriptionAttribute"/> text on the parameter, when present.</param>
internal sealed record ToolParameterSchema(
    string Name,
    string Type,
    bool Required,
    string? Description);
