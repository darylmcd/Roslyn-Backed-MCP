using System.Text.Json;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class HookConfigurationTests
{
    // ----- Shipped hooks/hooks.json (plugin-distributed; consumer-facing) -----

    [TestMethod]
    public void Shipped_PreToolUse_DoesNotContain_RoslynApplyTranscriptGate()
    {
        using var document = LoadShippedHooks();
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
    public void Shipped_PostToolUse_KeepsRoslynApplyVerificationReminder()
    {
        using var document = LoadShippedHooks();
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

    [TestMethod]
    public void Shipped_HooksJson_HasNoCommandBasedHooks()
    {
        // Regression guard for `roslyn-mcp-edit-hooks-mis-scoped-cross-repo` (2026-05-21).
        //
        // The shipped hooks/hooks.json must not register any command-based hooks: those
        // invoke `${CLAUDE_PROJECT_DIR}/eng/*.ps1` paths that only exist in this repo,
        // so consumer installs produce pwsh exit-64 failures on every matching tool call
        // (per the 2026-05-21 BioRemote cross-repo retrospective §F1: 8,398 silent
        // failures across 88 sessions in 11 repos over a 14-day window).
        //
        // Maintainer-only command-based hooks belong in .claude/settings.json — see the
        // LocalSettings_* tests below for their positive-assertion counterparts.
        using var document = LoadShippedHooks();
        foreach (var phase in new[] { "PreToolUse", "PostToolUse" })
        {
            foreach (var entry in GetHookEntries(document, phase))
            {
                foreach (var hook in entry.GetProperty("hooks").EnumerateArray())
                {
                    if (hook.TryGetProperty("type", out var type) && type.GetString() == "command")
                    {
                        var matcher = GetMatcher(entry);
                        var command = hook.TryGetProperty("command", out var c) ? c.GetString() ?? "(no command)" : "(no command)";
                        Assert.Fail(
                            $"Shipped hooks/hooks.json contains a command-based hook in {phase} (matcher='{matcher}', command='{command}'). " +
                            "Command-based hooks reference repo-local paths via ${CLAUDE_PROJECT_DIR} and fail with pwsh exit 64 in consumer repos. " +
                            "Move maintainer command-based hooks to .claude/settings.json (which is loaded only when CWD is this repo).");
                    }
                }
            }
        }
    }

    // ----- Repo-local .claude/settings.json (maintainer-only; not shipped) -----

    [TestMethod]
    public void LocalSettings_PreToolUse_KeepsReleaseManagedFileGuard()
    {
        using var document = LoadLocalSettings();
        var preToolUseHooks = GetHookEntries(document, "PreToolUse");

        var releaseGuard = preToolUseHooks.SingleOrDefault(entry =>
            GetMatcher(entry) == "Edit|Write|MultiEdit" &&
            GetCommands(entry).Any(command =>
                command.Contains("guard-release-managed-files", StringComparison.Ordinal)));

        Assert.AreEqual(JsonValueKind.Object, releaseGuard.ValueKind,
            "The Edit/Write/MultiEdit release-managed-file guard must stay configured in .claude/settings.json " +
            "(command-based hook calling eng/guard-release-managed-files.ps1). " +
            "Moved here from the shipped hooks/hooks.json in `roslyn-mcp-edit-hooks-mis-scoped-cross-repo` (2026-05-21).");
    }

    [TestMethod]
    public void LocalSettings_PostToolUse_KeepsVerifySkillsOnEditHook()
    {
        using var document = LoadLocalSettings();
        var postToolUseHooks = GetHookEntries(document, "PostToolUse");

        var verifySkillsHook = postToolUseHooks.SingleOrDefault(entry =>
            GetMatcher(entry) == "Edit|Write|MultiEdit" &&
            GetCommands(entry).Any(command =>
                command.Contains("verify-skills-on-edit", StringComparison.Ordinal)));

        Assert.AreEqual(JsonValueKind.Object, verifySkillsHook.ValueKind,
            "The Edit/Write/MultiEdit verify-skills-on-edit hook must stay configured in .claude/settings.json " +
            "(command-based hook calling eng/verify-skills-on-edit.ps1). " +
            "Moved here from the shipped hooks/hooks.json in `roslyn-mcp-edit-hooks-mis-scoped-cross-repo` (2026-05-21).");
    }

    // ----- Helpers -----

    private static JsonDocument LoadShippedHooks()
        => LoadJsonConfig(Path.Combine("hooks", "hooks.json"));

    private static JsonDocument LoadLocalSettings()
        => LoadJsonConfig(Path.Combine(".claude", "settings.json"));

    private static JsonDocument LoadJsonConfig(string relativePath)
    {
        var path = FindRepoRootFile(relativePath);
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
    {
        var root = document.RootElement;
        if (!root.TryGetProperty("hooks", out var hooks) ||
            !hooks.TryGetProperty(phase, out var phaseElement))
        {
            return Array.Empty<JsonElement>();
        }

        return phaseElement.EnumerateArray().ToList();
    }

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
