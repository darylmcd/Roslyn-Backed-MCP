using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the VS-MSBuild-required diagnostic detection added by
/// `workspace-toolchain-status-preflight` (BioRemote 2026-05): COM-reference and
/// .NET-Core-MSBuild-limitation messages should be downgraded from raw
/// <c>WORKSPACE_FAILURE</c> Errors to Warnings, and the
/// <see cref="WorkspaceStatusSummaryDto"/> should expose <c>isReady=false</c> with a
/// concrete remediation hint instead of the misleading "run dotnet restore" path.
/// </summary>
[TestClass]
public sealed class WorkspaceToolchainClassifierTests
{
    private static readonly string SampleLoadedPath = Path.Combine("work", "SampleSolution.slnx");

    private static WorkspaceStatusDto BaselineStatus(IReadOnlyList<DiagnosticDto> diagnostics, bool isStale = false) =>
        new(
            WorkspaceId: "abc",
            LoadedPath: SampleLoadedPath,
            WorkspaceVersion: 1,
            SnapshotToken: "snap",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: 2,
            DocumentCount: 10,
            Projects: [],
            IsLoaded: true,
            IsStale: isStale,
            WorkspaceDiagnostics: diagnostics);

    [TestMethod]
    public void Classify_ComReferenceMessage_ReturnsWarning()
    {
        // Realistic COM-reference failure messages from MSBuildWorkspace include the
        // ResolveComReference task name and a "type library" mention.
        var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
            WorkspaceDiagnosticKind.Failure,
            "ResolveComReference: Cannot register type library 'TLB1234' for COM interop.");
        Assert.AreEqual("Warning", severity,
            "COM-reference failures must downgrade to Warning so partial-load UX surfaces a remediation hint.");
    }

    [TestMethod]
    public void Classify_DotNetCoreMSBuildMessage_ReturnsWarning()
    {
        var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
            WorkspaceDiagnosticKind.Failure,
            "The .NET Core version of MSBuild does not support this task.");
        Assert.AreEqual("Warning", severity,
            ".NET Core MSBuild limitation messages must downgrade to Warning.");
    }

    [TestMethod]
    public void Classify_UnrelatedFailure_ReturnsError()
    {
        // Regression guard: arbitrary failure messages must remain Error so we don't
        // accidentally silence real workspace problems.
        var severity = WorkspaceDiagnosticSeverityClassifier.Classify(
            WorkspaceDiagnosticKind.Failure,
            "Project file is invalid: missing closing tag at line 42.");
        Assert.AreEqual("Error", severity,
            "Arbitrary failure messages must stay at Error severity.");
    }

    [TestMethod]
    public void IsVsMsbuildRequiredMessage_TypeLibrary_ReturnsTrue()
    {
        Assert.IsTrue(WorkspaceDiagnosticSeverityClassifier.IsVsMsbuildRequiredMessage(
            "Could not find type library."));
    }

    [TestMethod]
    public void IsVsMsbuildRequiredMessage_EmptyOrNull_ReturnsFalse()
    {
        Assert.IsFalse(WorkspaceDiagnosticSeverityClassifier.IsVsMsbuildRequiredMessage(string.Empty));
        Assert.IsFalse(WorkspaceDiagnosticSeverityClassifier.IsVsMsbuildRequiredMessage(null!));
    }

    /// <summary>
    /// End-to-end check that VS-MSBuild-required diagnostics flip <c>isReady</c> to false
    /// in the summary DTO and emit the concrete VS-MSBuild remediation hint instead of the
    /// misleading "run dotnet restore" path. Mirrors
    /// <c>From_UnresolvedAnalyzerWarning_AnalyzersReadyFalse_AndIsReadyFalse</c>.
    /// </summary>
    [TestMethod]
    public void From_VsMsbuildRequired_IsReadyFalse_HintContainsRemediation()
    {
        // After the classifier downgrades the failure, the diagnostic arrives at the DTO
        // with Id="WORKSPACE_FAILURE" (the ingress stamper does not know about VS-MSBuild)
        // and Severity="Warning". The DTO detects the condition by message content.
        var diagnostics = new DiagnosticDto[]
        {
            new(
                "WORKSPACE_FAILURE",
                "ResolveComReference: Cannot resolve COM reference 'MSXML2'.",
                "Warning",
                "Workspace",
                null, null, null, null, null),
        };

        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus(diagnostics));

        Assert.IsFalse(summary.AnalyzersReady,
            "AnalyzersReady must be false when a VS-MSBuild-required diagnostic is present.");
        Assert.IsFalse(summary.IsReady,
            "IsReady must require AnalyzersReady.");
        Assert.IsNotNull(summary.RestoreHint,
            "VS-MSBuild-required workspace must produce a remediation hint.");
        StringAssert.Contains(summary.RestoreHint, "Visual Studio MSBuild",
            "Hint must name the VS-MSBuild requirement explicitly.");
        StringAssert.Contains(summary.RestoreHint, "COM references",
            "Hint must mention COM references as the canonical remediation target.");
    }

    /// <summary>
    /// The VS-MSBuild branch must take priority over the legacy "suggest dotnet restore"
    /// path even when the diagnostic list also looks restore-shaped. Without this priority
    /// ordering, BioRemote-style sessions would still see the misleading restore hint.
    /// </summary>
    [TestMethod]
    public void From_VsMsbuildRequired_TakesPriorityOverRestoreSuggestion()
    {
        var diagnostics = new DiagnosticDto[]
        {
            new("WORKSPACE_FAILURE", "ResolveComReference failed for tlbimp output.", "Warning", "Workspace", null, null, null, null, null),
            new("CS0234", "Type 'A' could not be found", "Error", "Workspace", null, null, null, null, null),
            new("CS0234", "Type 'B' could not be found", "Error", "Workspace", null, null, null, null, null),
            new("CS0234", "Type 'C' could not be found", "Error", "Workspace", null, null, null, null, null),
        };

        var summary = WorkspaceStatusSummaryDto.From(BaselineStatus(diagnostics));

        Assert.IsNotNull(summary.RestoreHint);
        StringAssert.Contains(summary.RestoreHint, "Visual Studio MSBuild",
            "VS-MSBuild hint must win over the generic dotnet-restore suggestion.");
    }
}
