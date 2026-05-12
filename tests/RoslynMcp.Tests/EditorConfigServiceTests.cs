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
