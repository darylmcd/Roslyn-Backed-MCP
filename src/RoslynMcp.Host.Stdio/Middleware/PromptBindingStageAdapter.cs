using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Prompts;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Validates caller-owned prompt arguments before the SDK binder and prompt handler share an
/// exception boundary. This keeps malformed input distinguishable from a handler that happens to
/// throw the same public exception type without depending on SDK-private implementation frames.
/// </summary>
internal sealed class PromptBindingStageAdapter
{
    private static readonly PromptBindingStageAdapter s_default =
        new(typeof(RoslynPrompts).Assembly);

    private readonly FrozenDictionary<string, ParameterInfo[]> _parametersByPrompt;

    internal PromptBindingStageAdapter(Assembly promptAssembly)
    {
        ArgumentNullException.ThrowIfNull(promptAssembly);

        var registrations = new Dictionary<string, ParameterInfo[]>(StringComparer.Ordinal);
        foreach (var type in promptAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attribute?.Name is null)
                {
                    continue;
                }

                if (!registrations.TryAdd(
                        attribute.Name,
                        method.GetParameters()
                            .Where(PromptParameterClassifier.IsCallerInput)
                            .ToArray()))
                {
                    throw new InvalidOperationException(
                        $"Duplicate MCP prompt registration '{attribute.Name}'.");
                }
            }
        }

        _parametersByPrompt = registrations.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal static PromptBindingStageAdapter Default => s_default;

    internal void Validate(GetPromptRequestParams? request)
    {
        var promptName = request?.Name;
        if (promptName is null || !_parametersByPrompt.TryGetValue(promptName, out var parameters))
        {
            // Preserve the SDK's unknown-prompt protocol contract.
            return;
        }

        var arguments = request?.Arguments;
        foreach (var parameter in parameters)
        {
            if (arguments is null || !arguments.TryGetValue(parameter.Name!, out var value))
            {
                if (!parameter.HasDefaultValue)
                {
                    throw MissingArgument(promptName, parameter.Name!);
                }

                continue;
            }

            try
            {
                // Validate with the SAME options the SDK prompt binder uses. Program.cs registers
                // prompts via WithPromptsFromAssembly() without serializer options, so the SDK binds
                // with McpJsonUtilities.DefaultOptions (NumberHandling = AllowReadingFromString).
                // The MCP spec models prompt arguments as strings, so "19" for an int parameter is
                // valid input; strict default options would reject what the binder accepts. If the
                // host ever passes custom options to WithPromptsFromAssembly, follow them here.
                _ = JsonSerializer.Deserialize(
                    value.GetRawText(),
                    parameter.ParameterType,
                    McpJsonUtilities.DefaultOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw InvalidArgumentValue(promptName, parameter.Name!, parameter.ParameterType);
            }
        }
    }

    private static McpProtocolException MissingArgument(string promptName, string argumentName) =>
        new(
            $"Missing required argument '{argumentName}' for prompt '{promptName}'.",
            McpErrorCode.InvalidParams);

    // Never echo the caller-supplied value: it may carry secrets (see the sanitization contract).
    private static McpProtocolException InvalidArgumentValue(
        string promptName,
        string argumentName,
        Type parameterType) =>
        new(
            $"Invalid value for argument '{argumentName}' of prompt '{promptName}': " +
            $"expected {GetExpectedJsonType(parameterType)}.",
            McpErrorCode.InvalidParams);

    private static string GetExpectedJsonType(Type parameterType)
    {
        var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (effectiveType == typeof(string) || effectiveType == typeof(char) || effectiveType.IsEnum)
            return $"a JSON string ({effectiveType.Name})";
        if (effectiveType == typeof(bool))
            return "a JSON boolean (Boolean)";
        if (effectiveType.IsPrimitive || effectiveType == typeof(decimal))
            return $"a JSON number or numeric string ({effectiveType.Name})";
        if (effectiveType.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(effectiveType))
            return $"a JSON array ({effectiveType.Name})";
        return $"a JSON object ({effectiveType.Name})";
    }
}
