using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #8 — regression guard for `rename-preview-output-cap-high-fan-out-symbols`.
/// Jellyfin stress test §5 / §2 Bottleneck #1: rename_preview on IUserManager (233 refs)
/// returned ~98 KB of unified-diff text and exceeded the MCP output cap. The fix adds
/// a summary mode that replaces per-file UnifiedDiff with one-line summaries while the
/// stored Solution still carries every real edit.
/// </summary>
[TestClass]
public sealed class RenameSummaryModeTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Rename_Summary_Mode_Replaces_UnifiedDiff_With_OneLine_Summary()
    {
        var json = await RefactoringTools.PreviewRename(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            newName: "GetAllAnimalsRenamedBySummary",
            metadataName: "SampleLib.AnimalService.GetAllAnimals",
            summary: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"rename_preview summary=true should succeed. Got: {json}");

        var changes = doc.RootElement.GetProperty("changes");
        Assert.IsTrue(changes.GetArrayLength() > 0, "Must produce at least one change entry.");

        foreach (var change in changes.EnumerateArray())
        {
            var unifiedDiff = change.GetProperty("unifiedDiff").GetString();
            Assert.IsNotNull(unifiedDiff);
            StringAssert.StartsWith(
                unifiedDiff!,
                "# summary=true:",
                "Every change in summary mode must carry the compact summary marker, NOT a full unified diff.");

            // Guardrail: a diff with real `@@ ... @@` hunk headers or `+`/`-` content lines
            // would mean summary mode leaked the full payload through.
            Assert.IsFalse(
                unifiedDiff.Contains("@@ ", StringComparison.Ordinal),
                $"Summary mode must not include diff hunk markers. Got: {unifiedDiff}");
        }
    }

    [TestMethod]
    public async Task Rename_Summary_Apply_Still_Rewrites_All_References()
    {
        // Contract: even with summary=true, the stored preview Solution is complete, so
        // apply rewrites every reference. Use a throwaway workspace to avoid mutating the
        // shared one.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var localWorkspaceId = loadResult.WorkspaceId;

        try
        {
            var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
            var animalServicePath = Path.Combine(sampleLibDir, "AnimalService.cs");
            var preRename = await File.ReadAllTextAsync(animalServicePath);
            Assert.IsTrue(preRename.Contains("GetAllAnimals"), "Fixture sanity: pre-rename file must contain the method.");

            var json = await RefactoringTools.PreviewRename(
                WorkspaceExecutionGate,
                RefactoringService,
                localWorkspaceId,
                newName: "GetAllAnimalsItem8",
                metadataName: "SampleLib.AnimalService.GetAllAnimals",
                summary: true,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var token = doc.RootElement.GetProperty("previewToken").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(token));

            var applyResult = await RefactoringService.ApplyRefactoringAsync(token!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Summary-mode apply must succeed. Error: {applyResult.Error}");

            var postRename = await File.ReadAllTextAsync(animalServicePath);
            Assert.IsTrue(
                postRename.Contains("GetAllAnimalsItem8"),
                $"Apply must rewrite the symbol declaration even when the preview used summary mode. Post-apply:\n{postRename}");
            Assert.IsFalse(
                postRename.Contains("GetAllAnimals("),
                $"Apply must rename every reference even when the preview used summary mode. Post-apply:\n{postRename}");
        }
        finally
        {
            WorkspaceManager.Close(localWorkspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task Rename_Without_Summary_Still_Emits_Full_UnifiedDiff()
    {
        // Sanity: default behavior (summary=false) preserves the full diff payload.
        var json = await RefactoringTools.PreviewRename(
            WorkspaceExecutionGate,
            RefactoringService,
            WorkspaceId,
            newName: "GetAllAnimalsFullDiff",
            metadataName: "SampleLib.AnimalService.GetAllAnimals",
            summary: false,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var changes = doc.RootElement.GetProperty("changes");
        var anyHunkFound = false;
        foreach (var change in changes.EnumerateArray())
        {
            var unifiedDiff = change.GetProperty("unifiedDiff").GetString();
            if (unifiedDiff is not null && unifiedDiff.Contains("@@ ", StringComparison.Ordinal))
            {
                anyHunkFound = true;
                break;
            }
        }
        Assert.IsTrue(anyHunkFound, "Default summary=false must emit at least one real unified-diff hunk.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
