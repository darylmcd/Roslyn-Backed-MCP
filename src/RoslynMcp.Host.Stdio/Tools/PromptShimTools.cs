using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Prompts;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 4 (v1.18, <c>prompt-tools-exposable-to-agents</c>): generic dispatcher that exposes
/// every <see cref="McpServerPromptAttribute"/>-registered prompt as a <c>call_mcp_tool</c>-invocable
/// tool. Some MCP clients (Cursor, Claude Code in some configurations) cannot invoke prompts via
/// the dedicated <c>prompts/get</c> channel — this shim exposes the same content via the regular
/// tool channel so every host has a uniform path to the 19 prompt workflows.
/// </summary>
[McpServerToolType]
public static class PromptShimTools
{
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

        var parameterValues = await BuildParameterValuesAsync(method, services, parametersJson, ct).ConfigureAwait(false);

        object? result;
        try
        {
            result = method.Invoke(null, parameterValues);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }

        // Prompt methods return Task<IEnumerable<PromptMessage>> (or sometimes the
        // synchronous variant). Await the task and project messages into a JSON-friendly shape.
        var messages = await UnwrapPromptResultAsync(result).ConfigureAwait(false);
        var dto = new
        {
            promptName,
            parameterCount = method.GetParameters().Count(p => !IsServiceType(p.ParameterType) && p.ParameterType != typeof(CancellationToken)),
            messages = messages.Select(m => new
            {
                role = m.Role.ToString().ToLowerInvariant(),
                text = ExtractText(m),
            }).ToArray(),
        };
        return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
    }

    private static (MethodInfo? Method, McpServerPromptAttribute? Attribute) ResolvePromptMethod(string promptName)
    {
        var assembly = typeof(RoslynPrompts).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attr is null || attr.Name is null) continue;
                if (string.Equals(attr.Name, promptName, StringComparison.Ordinal))
                {
                    return (method, attr);
                }
            }
        }
        return (null, null);
    }

    private static IEnumerable<string> EnumeratePromptNames()
    {
        var assembly = typeof(RoslynPrompts).Assembly;
        var names = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attr?.Name is not null) names.Add(attr.Name);
            }
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static async Task<object?[]> BuildParameterValuesAsync(
        MethodInfo method, IServiceProvider services, string parametersJson, CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // dr-9-7-bug-json-parse-surfaces-stack-trace: a raw JsonException bubbling out of this
        // method lands in ToolErrorHandler.ClassifyError without the InvocationWrapper guard,
        // so it falls through to "InternalError" and the client sees a stack trace. Wrap the
        // top-level parse + each per-parameter deserialize and re-throw as ArgumentException —
        // that maps to "InvalidArgument" via the exact-type handler dictionary and the message
        // tells the agent exactly which parameter broke.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"parametersJson is not valid JSON: {ex.Message}. Supply a JSON object (e.g. {{\"workspaceId\":\"…\"}}); use \"{{}}\" to omit all parameters.",
                nameof(parametersJson),
                ex);
        }

        using (doc)
        {
            var rootObj = doc.RootElement;
            if (rootObj.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("parametersJson must be a JSON object.", nameof(parametersJson));

            var parameters = method.GetParameters();
            var values = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(CancellationToken))
                {
                    values[i] = ct;
                    continue;
                }
                if (IsServiceType(p.ParameterType))
                {
                    values[i] = services.GetRequiredService(p.ParameterType);
                    continue;
                }
                if (rootObj.TryGetProperty(p.Name!, out var element))
                {
                    try
                    {
                        values[i] = JsonSerializer.Deserialize(element.GetRawText(), p.ParameterType);
                    }
                    catch (JsonException ex)
                    {
                        throw new ArgumentException(
                            $"parametersJson property '{p.Name}' could not be deserialized into {p.ParameterType.Name}: {ex.Message}.",
                            nameof(parametersJson),
                            ex);
                    }
                }
                else if (p.HasDefaultValue)
                {
                    values[i] = p.DefaultValue;
                }
                else
                {
                    throw new ArgumentException(
                        $"Prompt parameter '{p.Name}' (type {p.ParameterType.Name}) is required but missing from parametersJson.",
                        nameof(parametersJson));
                }
            }
            return values;
        }
    }

    private static bool IsServiceType(Type t) =>
        t.IsInterface || t.Namespace?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true;

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
}
