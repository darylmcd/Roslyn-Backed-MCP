using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for <c>set-editorconfig-option-round-trip-asymmetry</c>:
/// <see cref="IEditorConfigService.SetOptionAsync"/> writes to
/// <c>[*.{cs,csx,cake}]</c> but the pre-fix section matcher in
/// <see cref="IEditorConfigService.GetOptionsAsync"/>'s on-disk supplement
/// did not recognize brace-expansion glob sections, so keys for unloaded
/// analyzer ids (e.g. <c>CA9999</c>) were silently dropped on the following read.
/// </summary>
[TestClass]
public sealed class EditorConfigServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SetThenGet_UnloadedAnalyzerId_IsReturned()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // CA9999 is not a real analyzer id; no loaded analyzer will report it via
        // Roslyn's AnalyzerConfigOptionsProvider. The key must still come back from
        // the on-disk union supplement.
        const string unloadedKey = "dotnet_diagnostic.CA9999.severity";
        const string value = "none";

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, unloadedKey, value, "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(setResult.EditorConfigPath));

        var options = await EditorConfigService.GetOptionsAsync(
            workspaceId, dogFilePath, CancellationToken.None);

        var entry = options.Options.FirstOrDefault(o =>
            string.Equals(o.Key, unloadedKey, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(entry,
            $"Get must surface '{unloadedKey}' after Set writes it, even when no loaded analyzer reports the id. " +
            $"Returned keys: {string.Join(", ", options.Options.Select(o => o.Key))}");
        Assert.AreEqual(value, entry!.Value);
    }

    /// <summary>
    /// Regression test for <c>editorconfig-write-no-auto-invalidation</c>:
    /// <see cref="IEditorConfigService.GetOptionsAsync"/> must return the value that
    /// <see cref="IEditorConfigService.SetOptionAsync"/> just wrote for a <em>known</em>
    /// key (one that Roslyn's <c>AnalyzerConfigOptionsProvider</c> already has in its
    /// cached workspace snapshot) — without requiring a <c>workspace_reload</c> call
    /// in between.
    /// </summary>
    [TestMethod]
    public async Task SetThenGet_KnownKey_ReturnsNewValueWithoutReload()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // indent_size is a well-known key that Roslyn's AnalyzerConfigOptionsProvider
        // enumerates. Writing a new value via SetOptionAsync must be visible on the
        // immediately-following GetOptionsAsync call without an intervening workspace_reload.
        const string knownKey = "indent_size";
        const string newValue = "4";

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, knownKey, newValue, "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(setResult.EditorConfigPath));

        // Intentionally NOT calling workspace_reload — that is the bug surface.
        var options = await EditorConfigService.GetOptionsAsync(
            workspaceId, dogFilePath, CancellationToken.None);

        var entry = options.Options.FirstOrDefault(o =>
            string.Equals(o.Key, knownKey, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(entry,
            $"Get must surface '{knownKey}' after Set writes it. " +
            $"Returned keys: {string.Join(", ", options.Options.Select(o => o.Key))}");
        Assert.AreEqual(newValue, entry!.Value,
            $"Get must return the newly-written value '{newValue}' without a workspace_reload. " +
            $"Actual value returned: '{entry.Value}'. This indicates the Roslyn-cached snapshot " +
            $"was returned instead of the on-disk value.");
    }

    /// <summary>
    /// Regression test for <c>set-editorconfig-option-duplicate-key-append</c> (gh #735):
    /// <see cref="IEditorConfigService.SetOptionAsync"/> previously matched existing keys
    /// with <c>StartsWith(key + " =", ...)</c>, which silently missed the no-space variant
    /// <c>key=value</c> common in hand-edited or IDE-generated <c>.editorconfig</c> files.
    /// When the predicate missed, the writer fell through to <c>Insert</c> and appended
    /// a duplicate key line on every subsequent call. The fix splits each line on the
    /// first <c>=</c> and compares the trimmed key portion case-insensitively, so the
    /// writer always upserts in place regardless of whitespace around <c>=</c>.
    /// </summary>
    [TestMethod]
    public async Task SetOptionAsync_SecondCallSameKeyValue_NoOp()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        const string key = "dotnet_diagnostic.CA1234.severity";
        const string firstValue = "warning";
        const string secondValue = "error";

        var firstResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, firstValue, "set_editorconfig_option", CancellationToken.None);
        Assert.IsTrue(File.Exists(firstResult.EditorConfigPath));

        // Second call: identical key + value must be an in-place upsert (no duplicate line).
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, firstValue, "set_editorconfig_option", CancellationToken.None);

        var linesAfterIdempotent = await File.ReadAllLinesAsync(firstResult.EditorConfigPath, CancellationToken.None);
        var firstOccurrences = linesAfterIdempotent.Count(l =>
        {
            var trimmed = l.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
                return false;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) return false;
            return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
        });
        Assert.AreEqual(1, firstOccurrences,
            $"After two identical SetOptionAsync calls, key '{key}' must appear exactly once. " +
            $"File content:\n{string.Join("\n", linesAfterIdempotent)}");

        // Third call: same key, different value. Still exactly one occurrence; value updated.
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, secondValue, "set_editorconfig_option", CancellationToken.None);

        var linesAfterUpdate = await File.ReadAllLinesAsync(firstResult.EditorConfigPath, CancellationToken.None);
        var matchingLines = linesAfterUpdate.Where(l =>
        {
            var trimmed = l.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
                return false;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) return false;
            return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        Assert.AreEqual(1, matchingLines.Count,
            $"After three SetOptionAsync calls (same key), key '{key}' must still appear exactly once. " +
            $"File content:\n{string.Join("\n", linesAfterUpdate)}");

        // Verify the value was updated to secondValue.
        var updatedLine = matchingLines[0].Trim();
        var updatedEqIdx = updatedLine.IndexOf('=');
        var updatedValue = updatedLine[(updatedEqIdx + 1)..].Trim();
        Assert.AreEqual(secondValue, updatedValue,
            $"Third call must have updated the value to '{secondValue}'. Actual: '{updatedValue}'.");
    }

    /// <summary>
    /// Regression test for <c>set-editorconfig-option-duplicate-key-append</c> (gh #735):
    /// hand-edited or IDE-generated <c>.editorconfig</c> files commonly write keys as
    /// <c>key=value</c> with no surrounding whitespace. The pre-fix matcher used
    /// <c>StartsWith(key + " =", ...)</c> and missed this variant, so a subsequent
    /// <c>SetOptionAsync</c> call would append a duplicate. After the fix, the writer
    /// splits each line on the first <c>=</c> and recognizes the existing key,
    /// replacing it in place with the canonical <c>key = value</c> format.
    /// </summary>
    [TestMethod]
    public async Task SetOptionAsync_NoSpaceVariantOnDisk_ReplacedInPlace()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Pre-seed an .editorconfig with the [*.{cs,csx,cake}] section and a no-space
        // entry that mirrors what a hand-edit or IDE generator typically produces.
        var editorconfigPath = Path.Combine(workspace.RootPath, ".editorconfig");
        const string key = "dotnet_diagnostic.CA9876.severity";
        await File.WriteAllLinesAsync(editorconfigPath,
            ["[*.{cs,csx,cake}]", $"{key}=warning"], CancellationToken.None);

        // SetOptionAsync must recognize the existing no-space entry and replace it in place.
        await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, key, "error", "set_editorconfig_option", CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(editorconfigPath, CancellationToken.None);
        var matchingLines = lines.Where(l =>
        {
            var trimmed = l.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';' || trimmed[0] == '[')
                return false;
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) return false;
            return string.Equals(trimmed[..eqIndex].Trim(), key, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        Assert.AreEqual(1, matchingLines.Count,
            $"After SetOptionAsync against a no-space pre-existing entry, key '{key}' must appear exactly once. " +
            $"File content:\n{string.Join("\n", lines)}");

        var updatedLine = matchingLines[0].Trim();
        var updatedEqIdx = updatedLine.IndexOf('=');
        var updatedValue = updatedLine[(updatedEqIdx + 1)..].Trim();
        Assert.AreEqual("error", updatedValue,
            $"No-space pre-existing entry must have been replaced with the new value. Actual: '{updatedValue}'.");
    }

    [TestMethod]
    public void SectionMatchesCSharp_RecognizesCommonGlobs()
    {
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.cs]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.{cs,csx,cake}]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.{vb,cs}]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[**.cs]"));
        Assert.IsTrue(EditorConfigService.SectionMatchesCSharp("[*.csx]"));

        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("[*.vb]"));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("[*.{vb,fs}]"));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("[*.json]"));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp(""));
        Assert.IsFalse(EditorConfigService.SectionMatchesCSharp("not-a-section"));
    }
}
