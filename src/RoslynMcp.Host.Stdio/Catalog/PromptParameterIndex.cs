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

                var parameters = BuildEntries(method);
                dict[attr.Name] = parameters;
            }
        }

        return dict;
    }

    internal static IReadOnlyList<PromptParameterEntry> BuildEntries(MethodInfo method) =>
        method.GetParameters()
            .Where(PromptParameterClassifier.IsCallerInput)
            .Select(BuildEntry)
            .ToArray();

    private static PromptParameterEntry BuildEntry(ParameterInfo parameter)
    {
        var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var typeName = CatalogTypeNameFormatter.FormatTypeName(parameter.ParameterType);
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

}
