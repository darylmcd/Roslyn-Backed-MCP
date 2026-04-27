using System.Text.Json;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class HookConfigurationTests
{
    [TestMethod]
    public void PreToolUse_DoesNotContain_RoslynApplyTranscriptGate()
    {
        using var document = LoadHooksConfig();
        var preToolUseHooks = GetHookEntries(document, "PreToolUse");

        var transcriptApplyGate = preToolUseHooks.FirstOrDefault(entry =>
            GetMatcher(entry).StartsWith("mcp__roslyn__", StringComparison.Ordinal) &&
            GetMatcher(entry).Contains("_apply", StringComparison.Ordinal) &&
            GetPromptTexts(entry).Any(prompt =>
                prompt.Contains("recent conversation", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("valid preview evidence", StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(JsonValueKind.Undefined, transcriptApplyGate.ValueKind,
            "Roslyn apply tools must rely on previewToken/tool-level validation, not transcript-scanning PreToolUse prompts.");
    }

    [TestMethod]
    public void PreToolUse_KeepsReleaseManagedFileGuard()
    {
        using var document = LoadHooksConfig();
        var preToolUseHooks = GetHookEntries(document, "PreToolUse");

        var releaseGuard = preToolUseHooks.SingleOrDefault(entry =>
            GetMatcher(entry) == "Edit|Write|MultiEdit" &&
            GetCommands(entry).Any(command =>
                command.Contains("guard-release-managed-files", StringComparison.Ordinal)));

        Assert.AreEqual(JsonValueKind.Object, releaseGuard.ValueKind,
            "The Edit/Write/MultiEdit release-managed file guard must stay configured (command-based hook calling eng/guard-release-managed-files.ps1).");
    }

    [TestMethod]
    public void PostToolUse_KeepsRoslynApplyVerificationReminder()
    {
        using var document = LoadHooksConfig();
        var postToolUseHooks = GetHookEntries(document, "PostToolUse");

        var verificationReminder = postToolUseHooks.SingleOrDefault(entry =>
            GetMatcher(entry).StartsWith("mcp__roslyn__", StringComparison.Ordinal) &&
            GetMatcher(entry).Contains("rename_apply", StringComparison.Ordinal) &&
            GetPromptTexts(entry).Any(prompt =>
                prompt.Contains("compile_check", StringComparison.Ordinal) &&
                prompt.Contains("verification", StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(JsonValueKind.Object, verificationReminder.ValueKind,
            "The post-apply verification reminder must remain after removing the transcript gate.");
    }

    private static JsonDocument LoadHooksConfig()
    {
        var path = FindRepoRootFile(Path.Combine("hooks", "hooks.json"));
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string FindRepoRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }

    private static IReadOnlyList<JsonElement> GetHookEntries(JsonDocument document, string phase)
        => document.RootElement
            .GetProperty("hooks")
            .GetProperty(phase)
            .EnumerateArray()
            .ToList();

    private static string GetMatcher(JsonElement entry)
        => entry.GetProperty("matcher").GetString() ?? string.Empty;

    private static IEnumerable<string> GetPromptTexts(JsonElement entry)
    {
        foreach (var hook in entry.GetProperty("hooks").EnumerateArray())
        {
            if (hook.TryGetProperty("type", out var type) &&
                type.GetString() == "prompt" &&
                hook.TryGetProperty("prompt", out var prompt))
            {
                yield return prompt.GetString() ?? string.Empty;
            }
        }
    }

    private static IEnumerable<string> GetCommands(JsonElement entry)
    {
        foreach (var hook in entry.GetProperty("hooks").EnumerateArray())
        {
            if (hook.TryGetProperty("type", out var type) &&
                type.GetString() == "command" &&
                hook.TryGetProperty("command", out var command))
            {
                yield return command.GetString() ?? string.Empty;
            }
        }
    }
}
