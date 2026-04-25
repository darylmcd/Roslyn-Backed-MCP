using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// get-prompt-text-publish-parameter-schema: cached reflection over every
/// <see cref="McpServerPromptAttribute"/>-attributed method in the Host.Stdio assembly,
/// projecting each prompt's user-facing parameters (i.e. excluding DI services and
/// <see cref="CancellationToken"/>) into a JSON-friendly schema list.
/// <para>
/// The catalog publishes this list per prompt via
/// <c>roslyn://server/catalog/prompts/{offset}/{limit}</c> so callers can build
/// <c>parametersJson</c> for <c>get_prompt_text</c> without a 2-roundtrip learn-then-invoke
/// loop. Reflection runs once at first access; the dictionary is immutable thereafter.
/// </para>
/// </summary>
internal static class PromptParameterIndex
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<PromptParameterEntry>>> s_index =
        new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Returns the cached parameter schema for <paramref name="promptName"/>, or an empty list
    /// when the prompt has no user-facing parameters or is not registered. Never returns
    /// <see langword="null"/> so callers can iterate without a null check.
    /// </summary>
    public static IReadOnlyList<PromptParameterEntry> GetParameters(string promptName)
    {
        if (string.IsNullOrEmpty(promptName)) return Array.Empty<PromptParameterEntry>();
        return s_index.Value.TryGetValue(promptName, out var parameters)
            ? parameters
            : Array.Empty<PromptParameterEntry>();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<PromptParameterEntry>> BuildIndex()
    {
        // Anchor on a known prompt-host type so we walk the same assembly that MCP discovery uses.
        var assembly = typeof(Prompts.RoslynPrompts).Assembly;
        var dict = new Dictionary<string, IReadOnlyList<PromptParameterEntry>>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            // Prompt methods are public static (per [McpServerPromptType] convention) but include
            // NonPublic for forward-compatibility with internal-test prompts.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attr?.Name is null) continue;

                var parameters = method.GetParameters()
                    .Where(p => !IsServiceType(p.ParameterType) && p.ParameterType != typeof(CancellationToken))
                    .Select(BuildEntry)
                    .ToArray();
                dict[attr.Name] = parameters;
            }
        }

        return dict;
    }

    private static PromptParameterEntry BuildEntry(ParameterInfo parameter)
    {
        var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var typeName = FormatTypeName(parameter.ParameterType);
        var required = !parameter.HasDefaultValue;
        var defaultValue = parameter.HasDefaultValue ? FormatDefaultValue(parameter.DefaultValue) : null;

        return new PromptParameterEntry(
            Name: parameter.Name ?? string.Empty,
            Type: typeName,
            Required: required,
            DefaultValue: defaultValue,
            Description: description);
    }

    /// <summary>
    /// Returns a stable, JSON-friendly type label. Nullable value types collapse to
    /// <c>{type}?</c>; reference types (which are nullable in the C# 8+ annotation model but
    /// which reflection cannot fully introspect for nullable-annotation context) keep the bare
    /// name. Generic types format as <c>List&lt;string&gt;</c> rather than CLR backtick-arity.
    /// </summary>
    private static string FormatTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return FormatTypeName(underlying) + "?";

        if (type.IsGenericType)
        {
            var name = type.Name;
            var tickIdx = name.IndexOf('`', StringComparison.Ordinal);
            if (tickIdx >= 0) name = name[..tickIdx];
            var args = type.GetGenericArguments().Select(FormatTypeName);
            return $"{name}<{string.Join(", ", args)}>";
        }

        // Map common primitives to their C# keyword form for readability.
        return type.FullName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Boolean" => "bool",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Decimal" => "decimal",
            "System.Byte" => "byte",
            "System.Object" => "object",
            _ => type.Name,
        };
    }

    /// <summary>
    /// Project the <see cref="ParameterInfo.DefaultValue"/> sentinel onto a JSON-stable form.
    /// Strings stay as strings; numbers and booleans stay as their unboxed value;
    /// <see cref="DBNull"/> (the BCL sentinel for "no default supplied") and <see langword="null"/>
    /// both collapse to JSON <c>null</c>.
    /// </summary>
    private static object? FormatDefaultValue(object? defaultValue)
    {
        if (defaultValue is null || defaultValue is DBNull) return null;
        return defaultValue;
    }

    /// <summary>
    /// Mirrors <c>PromptShimTools.IsServiceType</c>: prompts receive DI services either via
    /// interface-typed parameters or via the <c>Microsoft.Extensions</c> namespace family. Both
    /// are resolved by the host at invoke time and must NOT appear in the published schema.
    /// </summary>
    private static bool IsServiceType(Type t) =>
        t.IsInterface || t.Namespace?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true;
}
