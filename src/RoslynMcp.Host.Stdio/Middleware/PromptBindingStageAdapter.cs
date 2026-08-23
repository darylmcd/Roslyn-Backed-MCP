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
                    throw InvalidParameters(promptName);
                }

                continue;
            }

            try
            {
                _ = JsonSerializer.Deserialize(value.GetRawText(), parameter.ParameterType);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw InvalidParameters(promptName);
            }
        }
    }

    private static McpProtocolException InvalidParameters(string promptName) =>
        new(
            $"Invalid parameters for prompt '{promptName}'. " +
            "Provide every required argument using the advertised parameter types.",
            McpErrorCode.InvalidParams);
}
