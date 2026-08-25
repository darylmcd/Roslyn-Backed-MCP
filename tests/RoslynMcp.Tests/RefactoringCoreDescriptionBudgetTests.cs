using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the refactoring-core tool surface: these six method-level
/// [Description] strings are capability statements, not guidance essays. Refusal reasons
/// live in the services' runtime error messages and budget semantics live in the
/// parameter [Description]s, so the method text must stay short.
/// </summary>
[TestClass]
public sealed class RefactoringCoreDescriptionBudgetTests
{
    private const int MaxDescriptionCharacters = 220;

    private static readonly string[] RatchetedToolNames =
    [
        "change_signature_preview",
        "extract_method_apply",
        "extract_method_preview",
        "extract_shared_expression_to_helper_preview",
        "get_syntax_tree",
        "parameter_object_preview",
    ];

    [TestMethod]
    public void RefactoringCoreToolDescriptions_AreNonEmptyAndWithinSliceBudget()
    {
        var descriptions = typeof(ServerTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => new
            {
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(),
            })
            .Where(entry => entry.Tool?.Name is not null)
            .GroupBy(entry => entry.Tool!.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Description?.Description, StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var toolName in RatchetedToolNames)
        {
            if (!descriptions.TryGetValue(toolName, out var description))
            {
                violations.Add($"{toolName}: no [McpServerTool] method found");
                continue;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                violations.Add($"{toolName}: description is missing or empty");
                continue;
            }

            if (description.Length > MaxDescriptionCharacters)
            {
                violations.Add($"{toolName}: {description.Length} chars");
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            $"Refactoring-core tool descriptions must be non-empty and <= {MaxDescriptionCharacters} characters. " +
            "Violations:\n  " + string.Join("\n  ", violations));
    }
}
