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
            workspaceId, dogFilePath, unloadedKey, value, CancellationToken.None);
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
