using System.Collections.Frozen;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Prompts;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Carries an explicitly constructed, value-free prompt binding correction through the shared
/// tool error boundary. Only this type is allowed to publish its message verbatim.
/// </summary>
internal sealed class PromptParameterBindingException : ArgumentException
{
    public PromptParameterBindingException(string publicMessage, Exception innerException)
        : base(publicMessage, "parametersJson", innerException)
    {
        PublicMessage = publicMessage;
    }

    internal string PublicMessage { get; }
}

/// <summary>
/// Item 4 (v1.18, <c>prompt-tools-exposable-to-agents</c>): generic dispatcher that exposes
/// every <see cref="McpServerPromptAttribute"/>-registered prompt as a <c>call_mcp_tool</c>-invocable
/// tool. Some MCP clients (Cursor, Claude Code in some configurations) cannot invoke prompts via
/// the dedicated <c>prompts/get</c> channel — this shim exposes the same content via the regular
/// tool channel so every host has a uniform path to every registered prompt workflow.
/// </summary>
[McpServerToolType]
public static class PromptShimTools
{
    private static readonly Lazy<FrozenDictionary<string, PromptMethodRegistration>> _promptMethodIndex =
        new(BuildPromptMethodIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _promptIndexBuildCount;

    internal static int PromptIndexBuildCount => Volatile.Read(ref _promptIndexBuildCount);

    [McpServerTool(Name = "get_prompt_text", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("prompts", "experimental", true, false,
        "Render any registered MCP prompt as plain text. Pass the prompt name plus a JSON object of the prompt's parameters; returns { messages: [{role, text}], promptName, parameterCount }."),
     Description("Render any registered MCP prompt as plain text via the regular tool channel. Useful for clients that cannot invoke prompts via prompts/get directly. Pass `promptName` (e.g. \"explain_error\", \"refactor_and_validate\") and a `parametersJson` object containing the prompt's named parameters. Returns the rendered message list as JSON.")]
    public static async Task<string> GetPromptText(
        IServiceProvider services,
        [Description("The name of the prompt as registered with [McpServerPrompt(Name = \"...\")]. Use list_prompts on the resources channel or read roslyn://server/catalog for the full list.")] string promptName,
        [Description("JSON object of named parameters the prompt expects (e.g. {\"workspaceId\":\"...\",\"filePath\":\"...\",\"line\":12}). Service-typed parameters (IDiagnosticService, IWorkspaceManager, etc.) are resolved automatically and must NOT appear here.")] string parametersJson = "{}",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(promptName))
            throw new ArgumentException("promptName is required.", nameof(promptName));

        var (method, attribute) = ResolvePromptMethod(promptName);
        if (method is null || attribute is null)
        {
            throw new ArgumentException(
                $"Prompt '{promptName}' not found. Available prompts: " +
                string.Join(", ", EnumeratePromptNames()),
                nameof(promptName));
        }

        var parameterValues = BuildParameterValues(method, services, parametersJson, ct);

        object? result;
        try
        {
            result = method.Invoke(null, parameterValues);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }

        // Prompt methods return Task<IEnumerable<PromptMessage>> (or sometimes the
        // synchronous variant). Await the task and project messages into a JSON-friendly shape.
        var messages = await UnwrapPromptResultAsync(result).ConfigureAwait(false);

        // get-prompt-text-publish-parameter-schema: surface the same parameter schema the
        // catalog publishes so a caller introspecting via the tool channel (no resource/list
        // round-trip) gets the names/types/required flags in one hop.
        var parameterSchema = PromptParameterIndex.GetParameters(promptName);
        var dto = new
        {
            promptName,
            parameterCount = parameterSchema.Count,
            parameters = parameterSchema,
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                text = ExtractText(m),
            }).ToArray(),
        };
        return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
    }

    internal static (MethodInfo? Method, McpServerPromptAttribute? Attribute) ResolvePromptMethod(
        string promptName)
    {
        return _promptMethodIndex.Value.TryGetValue(promptName, out var registration)
            ? (registration.Method, registration.Attribute)
            : (null, null);
    }

    private static IEnumerable<string> EnumeratePromptNames() =>
        _promptMethodIndex.Value.Keys.OrderBy(static name => name, StringComparer.Ordinal);

    private static FrozenDictionary<string, PromptMethodRegistration> BuildPromptMethodIndex()
    {
        Interlocked.Increment(ref _promptIndexBuildCount);
        var assembly = typeof(RoslynPrompts).Assembly;
        var registrations = new Dictionary<string, PromptMethodRegistration>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attribute?.Name is null)
                {
                    continue;
                }

                if (!registrations.TryAdd(
                        attribute.Name,
                        new PromptMethodRegistration(method, attribute)))
                {
                    throw new InvalidOperationException(
                        $"Duplicate MCP prompt registration '{attribute.Name}'.");
                }
            }
        }

        return registrations.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal static object?[] BuildParameterValues(
        MethodInfo method, IServiceProvider services, string parametersJson, CancellationToken ct)
    {
        using var doc = ParseParametersDocument(parametersJson);
        var rootObj = doc.RootElement;

        var parameters = method.GetParameters();
        EnsureRequiredParametersPresent(method.Name, parameters, rootObj);

        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            values[i] = ResolveParameterValue(parameters[i], services, rootObj, ct);
        }
        return values;
    }

    // Convert parser diagnostics into an explicitly safe binding exception. The shared tool error
    // boundary publishes only PromptParameterBindingException.PublicMessage; the JsonException
    // remains available as the inner exception for server-side diagnostics. Callers own disposal
    // of the returned JsonDocument on success; the non-object branch disposes before throwing.
    private static JsonDocument ParseParametersDocument(string parametersJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
        }
        catch (JsonException ex)
        {
            throw new PromptParameterBindingException(
                "parametersJson must contain a valid JSON object. " +
                "Example: {\"workspaceId\":\"workspace-1\"}; use \"{}\" to omit all parameters.",
                ex);
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw new ArgumentException("parametersJson must be a JSON object.", "parametersJson");
        }

        return doc;
    }

    private static void EnsureRequiredParametersPresent(
        string methodName, ParameterInfo[] parameters, JsonElement rootObj)
    {
        var missingRequired = parameters
            .Where(PromptParameterClassifier.IsCallerInput)
            .Where(p => !p.HasDefaultValue)
            .Where(p => !rootObj.TryGetProperty(p.Name!, out _))
            .Select(p => p.Name!)
            .ToArray();
        if (missingRequired.Length > 0)
        {
            throw new ArgumentException(
                $"Prompt '{methodName}' is missing required parameters in parametersJson: {string.Join(", ", missingRequired)}.",
                "parametersJson");
        }
    }

    private static object? ResolveParameterValue(
        ParameterInfo p, IServiceProvider services, JsonElement rootObj, CancellationToken ct)
    {
        if (p.ParameterType == typeof(CancellationToken))
            return ct;
        if (PromptParameterClassifier.IsServiceType(p.ParameterType))
            return services.GetRequiredService(p.ParameterType);
        if (rootObj.TryGetProperty(p.Name!, out var element))
            return DeserializeParameterValue(p, element);
        if (p.HasDefaultValue)
            return p.DefaultValue;

        throw new ArgumentException(
            $"Prompt parameter '{p.Name}' (type {p.ParameterType.Name}) is required but missing from parametersJson.",
            "parametersJson");
    }

    private static object? DeserializeParameterValue(ParameterInfo p, JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize(element.GetRawText(), p.ParameterType);
        }
        catch (JsonException ex)
        {
            throw new PromptParameterBindingException(
                $"parametersJson property '{p.Name}' must be compatible with " +
                $"{GetExpectedJsonType(p.ParameterType)}. " +
                $"Example: {{\"{p.Name}\":{GetExpectedJsonValue(p.ParameterType)}}}.",
                ex);
        }
    }

    private static string GetExpectedJsonType(Type parameterType)
    {
        var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (effectiveType == typeof(string) || effectiveType == typeof(char) || effectiveType.IsEnum)
            return $"a JSON string ({effectiveType.Name})";
        if (effectiveType == typeof(bool))
            return "a JSON boolean (Boolean)";
        if (effectiveType.IsPrimitive || effectiveType == typeof(decimal))
            return $"a JSON number ({effectiveType.Name})";
        if (effectiveType.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(effectiveType))
            return $"a JSON array ({effectiveType.Name})";
        return $"a JSON object ({effectiveType.Name})";
    }

    private static string GetExpectedJsonValue(Type parameterType)
    {
        var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (effectiveType == typeof(string) || effectiveType == typeof(char) || effectiveType.IsEnum)
            return "\"value\"";
        if (effectiveType == typeof(bool))
            return "true";
        if (effectiveType.IsPrimitive || effectiveType == typeof(decimal))
            return "1";
        if (effectiveType.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(effectiveType))
            return "[]";
        return "{}";
    }

    private static async Task<IEnumerable<PromptMessage>> UnwrapPromptResultAsync(object? result)
    {
        if (result is null) return Array.Empty<PromptMessage>();

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            // Pull Result via reflection to support Task<IEnumerable<PromptMessage>>.
            var resultProp = task.GetType().GetProperty("Result");
            var inner = resultProp?.GetValue(task);
            if (inner is IEnumerable<PromptMessage> messages) return messages;
            return Array.Empty<PromptMessage>();
        }

        if (result is IEnumerable<PromptMessage> direct) return direct;
        return Array.Empty<PromptMessage>();
    }

    private static string ExtractText(PromptMessage message)
    {
        // PromptMessage.Content is a Content union; the text variant carries the prompt body.
        if (message.Content is TextContentBlock text) return text.Text ?? string.Empty;
        return message.Content?.ToString() ?? string.Empty;
    }

    private sealed record PromptMethodRegistration(
        MethodInfo Method,
        McpServerPromptAttribute Attribute);
}
