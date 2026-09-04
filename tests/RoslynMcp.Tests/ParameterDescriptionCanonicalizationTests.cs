using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the parameter-description canonicalization sweep.
/// </summary>
/// <remarks>
/// Scope is deliberately limited to the tool types swept so far. The shared
/// <see cref="ParameterDescriptionBudgetHarness"/> owns parameter discovery and assertions;
/// this slice owns only its type set and exact boilerplate forms.
/// </remarks>
[TestClass]
public sealed class ParameterDescriptionCanonicalizationTests
{
    private static readonly Type[] SweptToolTypes =
    [
        typeof(EditTools),
        typeof(FileOperationTools),
        typeof(MSBuildTools),
        typeof(TypeMoveTools),
        typeof(ValidationBundleTools),
        typeof(CrossProjectRefactoringTools),
        typeof(ChangeSignatureTools),
        typeof(ParameterObjectTools),
        typeof(RefactoringTools),
        typeof(RestructureTools),
        typeof(ScriptingTools),
        typeof(OperationTools),
    ];

    private static readonly ParameterDescriptionBudgetHarness.CanonicalFormExpectation[] CanonicalForms =
    [
        new(
            "workspaceId",
            _ => "Workspace session id from workspace_load.",
            entry => entry.ToolName != "workspace_fork_apply"),
        new(
            "previewToken",
            ExpectedPreviewTokenDescription),
        new(
            "filePath",
            ExpectedRequiredPathDescription,
            entry => !entry.Parameter.IsOptional),
        new(
            "sourceFilePath",
            ExpectedRequiredPathDescription,
            entry => !entry.Parameter.IsOptional),
        new(
            "targetFilePath",
            ExpectedRequiredPathDescription,
            entry => !entry.Parameter.IsOptional),
    ];

    [TestMethod]
    public void SweptTools_BoilerplateParameterDescriptions_UseCanonicalForm() =>
        ParameterDescriptionBudgetHarness.AssertCanonicalForms(SweptToolTypes, CanonicalForms);

    private static string ExpectedPreviewTokenDescription(
        ParameterDescriptionBudgetHarness.ToolParameterEntry entry)
    {
        if (entry.ToolName == "workspace_fork_apply")
        {
            return "Preview token from any *_preview tool.";
        }

        const string applySuffix = "_apply";
        return entry.ToolName.EndsWith(applySuffix, StringComparison.Ordinal)
            ? $"Preview token from {entry.ToolName[..^applySuffix.Length]}_preview."
            : $"<no exact preview-token form declared for {entry.ToolName}>";
    }

    private static string ExpectedRequiredPathDescription(
        ParameterDescriptionBudgetHarness.ToolParameterEntry entry) =>
        (entry.ToolName, entry.Parameter.Name) switch
        {
            ("apply_text_edit", "filePath") => "Absolute path to the file to edit.",
            ("create_file_preview", "filePath") => "Absolute path to the file to create.",
            ("delete_file_preview", "filePath") => "Absolute path to the file to delete.",
            ("move_file_preview", "sourceFilePath") => "Absolute path to the source file.",
            ("move_file_preview", "targetFilePath") => "Absolute path to the destination file.",
            ("move_type_to_file_preview", "sourceFilePath") =>
                "Absolute path to the source file containing the type.",
            ("move_type_to_project_preview", "sourceFilePath") =>
                "Absolute path to the source file containing the type.",
            ("extract_interface_cross_project_preview", "filePath") =>
                "Absolute path to the source file containing the type.",
            ("dependency_inversion_preview", "filePath") =>
                "Absolute path to the source file containing the concrete type.",
            (_, "filePath") => "Absolute path to the source file.",
            _ => $"<no exact required-path form declared for {entry.ToolName}.{entry.Parameter.Name}>",
        };
}
