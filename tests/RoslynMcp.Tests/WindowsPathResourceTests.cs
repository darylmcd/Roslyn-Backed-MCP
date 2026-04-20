using RoslynMcp.Host.Stdio.Resources;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for `file-resource-uri-windows-path-handling` (P4):
/// IT-Chat-Bot 2026-04-13 §14 reported that absolute Windows paths with `:` and
/// backslashes broke URI grammar. The handler now centralizes normalization in
/// <c>NormalizeFilePathForResource</c> and the tool description spells out that
/// URL-encoded form is preferred (raw paths may be rejected by some clients
/// before reaching the server).
///
/// These tests guard the server-side acceptance of every encoding shape a
/// reasonable client might send. Existing happy-path tests live in
/// <c>WorkspaceResourceTests</c>; these extend coverage to additional edge cases.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WindowsPathResourceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetSourceFile_UnencodedWindowsPath_Resolves()
    {
        var path = FindAnimalServicePath();
        var text = await WorkspaceResources.GetSourceFile(WorkspaceExecutionGate, WorkspaceManager, WorkspaceId, path, CancellationToken.None);
        StringAssert.Contains(text, "AnimalService");
    }

    [TestMethod]
    public async Task GetSourceFile_FullyEncodedWindowsPath_Resolves()
    {
        var path = FindAnimalServicePath();
        var encoded = Uri.EscapeDataString(path);
        // Sanity check the fixture: encoded form must NOT equal raw form on Windows
        // (it would prove the fixture is testing nothing).
        if (OperatingSystem.IsWindows())
        {
            Assert.AreNotEqual(path, encoded, "encoded path must differ from raw on Windows");
        }
        var text = await WorkspaceResources.GetSourceFile(WorkspaceExecutionGate, WorkspaceManager, WorkspaceId, encoded, CancellationToken.None);
        StringAssert.Contains(text, "AnimalService");
    }

    [TestMethod]
    public async Task GetSourceFile_ForwardSlashOnly_Resolves()
    {
        var path = FindAnimalServicePath();
        var forward = path.Replace('\\', '/');
        var text = await WorkspaceResources.GetSourceFile(WorkspaceExecutionGate, WorkspaceManager, WorkspaceId, forward, CancellationToken.None);
        StringAssert.Contains(text, "AnimalService");
    }

    [TestMethod]
    public async Task GetSourceFile_EncodedSeparatorsOnly_Resolves()
    {
        // Some clients only encode the separators (\\ and :) but not other characters.
        var path = FindAnimalServicePath();
        var partiallyEncoded = path.Replace(":", "%3A").Replace("\\", "%5C");
        var text = await WorkspaceResources.GetSourceFile(WorkspaceExecutionGate, WorkspaceManager, WorkspaceId, partiallyEncoded, CancellationToken.None);
        StringAssert.Contains(text, "AnimalService");
    }

    [TestMethod]
    public async Task GetSourceFile_EncodedForwardSlashes_NormalizesCorrectly()
    {
        // %2F is the URL-encoded form of '/'. After decode, the helper replaces
        // '/' with the platform separator, so encoded forward-slashes should also work.
        var path = FindAnimalServicePath();
        var forward = path.Replace('\\', '/');
        var encodedForward = forward.Replace("/", "%2F").Replace(":", "%3A");
        var text = await WorkspaceResources.GetSourceFile(WorkspaceExecutionGate, WorkspaceManager, WorkspaceId, encodedForward, CancellationToken.None);
        StringAssert.Contains(text, "AnimalService");
    }

    [TestMethod]
    public async Task GetSourceFileLines_AcceptsEncodedWindowsPath()
    {
        // The lines variant shares the normalizer — same encoding shapes must work.
        var path = FindAnimalServicePath();
        var encoded = Uri.EscapeDataString(path);
        var text = await WorkspaceResources.GetSourceFileLines(
            WorkspaceExecutionGate, WorkspaceManager, WorkspaceId, encoded, "1-3", CancellationToken.None);
        Assert.IsFalse(string.IsNullOrWhiteSpace(text));
        StringAssert.Contains(text, "// roslyn://workspace/");
    }

    private static string FindAnimalServicePath()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var path = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.Name, "AnimalService.cs", StringComparison.Ordinal))?.FilePath;
        return path ?? throw new AssertFailedException("AnimalService.cs not found");
    }
}
