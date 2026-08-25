using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace RoslynMcp.Tests;

/// <summary>
/// param-description-dedupe-code-action-suppression: locks the canonical one-liner forms for
/// <c>workspaceId</c> and <c>filePath</c> parameters across the code-action, suppression,
/// symbol-refactor, and bulk-refactoring tool surfaces so phrasing drift cannot re-accumulate.
/// Scoped to exactly the four in-scope types — NOT assembly-wide.
/// </summary>
[TestClass]
public sealed class ParamDescriptionCanonicalFormCodeActionSuppressionTests
{
    private const string CanonicalWorkspaceIdDescription =
        "The workspace session identifier returned by workspace_load";

    private const string CanonicalFilePathDescription = "Absolute path to the source file";

    /// <summary>
    /// (tool, param) pairs whose description is deliberately non-canonical because it carries
    /// call-accuracy information the canonical one-liner would lose.
    /// </summary>
    private static readonly HashSet<(string ToolName, string ParamName)> LoadBearingFilePathExceptions =
    [
        // Explains WHICH .editorconfig the server mutates — a real disambiguator.
        ("set_diagnostic_severity", "filePath"),
    ];

    private static readonly Type[] InScopeTypes =
    [
        typeof(CodeActionTools),
        typeof(SuppressionTools),
        typeof(SymbolRefactorTools),
        typeof(BulkRefactoringTools),
    ];

    [TestMethod]
    public void InScopeTools_WorkspaceIdAndFilePathDescriptions_UseCanonicalForms()
    {
        var failures = new List<string>();
        var workspaceIdCount = 0;
        var filePathCount = 0;
        var paramCount = 0;

        foreach (var (toolName, param) in EnumerateToolParameters())
        {
            paramCount++;
            var description = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;

            switch (param.Name)
            {
                case "workspaceId":
                    workspaceIdCount++;
                    if (!string.Equals(description, CanonicalWorkspaceIdDescription, StringComparison.Ordinal))
                        failures.Add($"({toolName}, workspaceId): expected \"{CanonicalWorkspaceIdDescription}\" but got \"{description}\".");
                    break;

                // Exactly "filePath" — not filePaths, sourceFilePath, hostRegistrationFile, etc.
                case "filePath":
                    if (LoadBearingFilePathExceptions.Contains((toolName, "filePath"))) break;
                    filePathCount++;
                    if (!string.Equals(description, CanonicalFilePathDescription, StringComparison.Ordinal))
                        failures.Add($"({toolName}, filePath): expected \"{CanonicalFilePathDescription}\" but got \"{description}\".");
                    break;
            }
        }

        Assert.IsTrue(paramCount > 0,
            "No tool parameters discovered on the in-scope types — reflection walk or tool registration changed.");
        Assert.IsTrue(workspaceIdCount > 0, "No 'workspaceId' parameters discovered on the in-scope types.");
        Assert.IsTrue(filePathCount > 0, "No non-exempt 'filePath' parameters discovered on the in-scope types.");
        Assert.AreEqual(0, failures.Count,
            "Non-canonical parameter descriptions:\n  " + string.Join("\n  ", failures));
    }

    [TestMethod]
    public void InScopeTools_EveryParameter_CarriesNonEmptyDescription()
    {
        var failures = new List<string>();
        var paramCount = 0;

        foreach (var (toolName, param) in EnumerateToolParameters())
        {
            paramCount++;
            var description = param.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (string.IsNullOrWhiteSpace(description))
                failures.Add($"({toolName}, {param.Name}): [Description] is missing or empty.");
        }

        Assert.IsTrue(paramCount > 0,
            "No tool parameters discovered on the in-scope types — reflection walk or tool registration changed.");
        Assert.AreEqual(0, failures.Count,
            "Tool parameters without a [Description]:\n  " + string.Join("\n  ", failures));
    }

    private static IEnumerable<(string ToolName, ParameterInfo Parameter)> EnumerateToolParameters()
    {
        foreach (var method in InScopeTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)))
        {
            var serverTool = method.GetCustomAttribute<McpServerToolAttribute>();
            if (serverTool is null) continue;

            var toolName = serverTool.Name ?? method.Name;
            foreach (var param in method.GetParameters())
            {
                if (!IsSchemaParameter(param)) continue;
                yield return (toolName, param);
            }
        }
    }

    /// <summary>
    /// True for parameters that surface in the tool's JSON input schema. Excludes the
    /// DI-injected host services (<see cref="McpServer"/> and service interfaces) and the
    /// trailing <see cref="CancellationToken"/>, none of which carry a [Description].
    /// </summary>
    private static bool IsSchemaParameter(ParameterInfo param)
    {
        var type = param.ParameterType;
        if (type == typeof(CancellationToken)) return false;
        if (type == typeof(McpServer)) return false;
        if (type.IsInterface) return false;
        return true;
    }
}
