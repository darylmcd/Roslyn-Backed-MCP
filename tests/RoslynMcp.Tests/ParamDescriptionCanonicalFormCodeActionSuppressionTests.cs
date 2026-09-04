using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Locks exact canonical boilerplate forms across the code-action, suppression,
/// symbol-refactor, and bulk-refactoring tool surfaces.
/// </summary>
[TestClass]
public sealed class ParamDescriptionCanonicalFormCodeActionSuppressionTests
{
    private static readonly Type[] InScopeTypes =
    [
        typeof(CodeActionTools),
        typeof(SuppressionTools),
        typeof(SymbolRefactorTools),
        typeof(BulkRefactoringTools),
    ];

    private static readonly ParameterDescriptionBudgetHarness.CanonicalFormExpectation[] CanonicalForms =
    [
        new(
            "workspaceId",
            _ => "The workspace session identifier returned by workspace_load"),
        new(
            "filePath",
            _ => "Absolute path to the source file",
            entry => entry.ToolName != "set_diagnostic_severity"),
    ];

    [TestMethod]
    public void InScopeTools_WorkspaceIdAndFilePathDescriptions_UseCanonicalForms() =>
        ParameterDescriptionBudgetHarness.AssertCanonicalForms(InScopeTypes, CanonicalForms);

    [TestMethod]
    public void InScopeTools_EveryParameter_CarriesNonEmptyDescription() =>
        ParameterDescriptionBudgetHarness.AssertAllSchemaParametersHaveNonEmptyDescriptions(InScopeTypes);
}
