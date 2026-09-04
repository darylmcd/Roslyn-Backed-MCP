using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace RoslynMcp.Tests;

/// <summary>
/// Shared reflection and assertion harness for parameter-description canonicalization slices.
/// Callers declare their swept tool types and exact boilerplate forms; descriptions carrying
/// tool-specific call guidance stay outside those expectations.
/// </summary>
internal static class ParameterDescriptionBudgetHarness
{
    internal sealed record CanonicalFormExpectation(
        string ParameterName,
        Func<ToolParameterEntry, string> ExpectedDescription,
        Func<ToolParameterEntry, bool>? IsBoilerplate = null);

    internal readonly record struct ToolParameterEntry(
        Type DeclaringType,
        MethodInfo Method,
        string ToolName,
        ParameterInfo Parameter,
        string? Description)
    {
        internal string Site => $"{DeclaringType.Name}.{Method.Name}({Parameter.Name})";
    }

    internal static void AssertCanonicalForms(
        IReadOnlyList<Type> sliceTypes,
        IReadOnlyList<CanonicalFormExpectation> expectations)
    {
        var parameters = EnumerateSchemaToolParameters(sliceTypes);
        Assert.IsGreaterThan(0, parameters.Length, "No schema parameters were discovered on the swept tool types.");

        var duplicateNames = expectations
            .GroupBy(expectation => expectation.ParameterName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.AreEqual(
            0,
            duplicateNames.Length,
            "Each parameter family must have one canonical-form expectation. Duplicates:\n  "
            + string.Join("\n  ", duplicateNames));

        var failures = new List<string>();

        foreach (var expectation in expectations)
        {
            var matches = parameters
                .Where(entry => string.Equals(
                    entry.Parameter.Name,
                    expectation.ParameterName,
                    StringComparison.Ordinal))
                .Where(entry => expectation.IsBoilerplate?.Invoke(entry) is not false)
                .ToArray();

            if (matches.Length == 0)
            {
                failures.Add($"{expectation.ParameterName}: no matching boilerplate parameter found");
                continue;
            }

            foreach (var match in matches)
            {
                var expectedDescription = expectation.ExpectedDescription(match);
                if (!string.Equals(expectedDescription, match.Description, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{match.Site}: expected \"{expectedDescription}\", "
                        + $"got \"{match.Description ?? "<missing>"}\"");
                }
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            "Boilerplate parameter descriptions must use an exact canonical form:\n  "
            + string.Join("\n  ", failures));
    }

    internal static void AssertAllSchemaParametersHaveNonEmptyDescriptions(IReadOnlyList<Type> sliceTypes)
    {
        var parameters = EnumerateSchemaToolParameters(sliceTypes);
        Assert.IsGreaterThan(0, parameters.Length, "No schema parameters were discovered on the swept tool types.");

        var missing = parameters
            .Where(entry => string.IsNullOrWhiteSpace(entry.Description))
            .Select(entry => entry.Site)
            .ToArray();

        Assert.AreEqual(
            0,
            missing.Length,
            "Tool parameters without a [Description]:\n  " + string.Join("\n  ", missing));
    }

    private static ToolParameterEntry[] EnumerateSchemaToolParameters(IReadOnlyList<Type> sliceTypes) =>
        sliceTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => new
            {
                Method = method,
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
            })
            .Where(entry => entry.Tool is not null)
            .SelectMany(entry => entry.Method.GetParameters().Select(parameter => new
            {
                entry.Method,
                entry.Tool,
                Parameter = parameter,
            }))
            .Where(entry => IsSchemaParameter(entry.Parameter))
            .Select(entry => new ToolParameterEntry(
                entry.Method.DeclaringType!,
                entry.Method,
                entry.Tool!.Name ?? entry.Method.Name,
                entry.Parameter,
                entry.Parameter.GetCustomAttribute<DescriptionAttribute>()?.Description))
            .OrderBy(entry => entry.ToolName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Parameter.Position)
            .ToArray();

    private static bool IsSchemaParameter(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        return type != typeof(CancellationToken)
            && type != typeof(McpServer)
            && !type.IsInterface;
    }
}

[TestClass]
public sealed class ParameterDescriptionBudgetHarnessTests
{
    [TestMethod]
    public void AssertCanonicalForms_RequiresExactText_AndSkipsLoadBearingSites()
    {
        var exactForms = new[]
        {
            new ParameterDescriptionBudgetHarness.CanonicalFormExpectation(
                "workspaceId",
                _ => "Canonical workspace description.",
                entry => entry.ToolName != "load_bearing_fixture"),
        };

        ParameterDescriptionBudgetHarness.AssertCanonicalForms([typeof(CanonicalFixtureTools)], exactForms);

        var failure = Assert.ThrowsExactly<AssertFailedException>(() =>
            ParameterDescriptionBudgetHarness.AssertCanonicalForms(
                [typeof(CanonicalFixtureTools)],
                [new("workspaceId", _ => "Canonical workspace description")]));
        StringAssert.Contains(failure.Message, "expected \"Canonical workspace description\"");
    }

    [TestMethod]
    public void AssertAllSchemaParametersHaveNonEmptyDescriptions_ReportsMissingDescription()
    {
        var failure = Assert.ThrowsExactly<AssertFailedException>(() =>
            ParameterDescriptionBudgetHarness.AssertAllSchemaParametersHaveNonEmptyDescriptions(
                [typeof(MissingDescriptionFixtureTools)]));

        StringAssert.Contains(failure.Message, "MissingDescriptionFixtureTools.MissingDescription(value)");
    }

    private static class CanonicalFixtureTools
    {
        [McpServerTool(Name = "canonical_fixture")]
        public static void Canonical(
            [Description("Canonical workspace description.")] string workspaceId,
            [Description("Tool-specific guidance remains unconstrained.")] string command)
        {
        }

        [McpServerTool(Name = "load_bearing_fixture")]
        public static void LoadBearing(
            [Description("Source workspace whose role must remain explicit.")] string workspaceId)
        {
        }
    }

    private static class MissingDescriptionFixtureTools
    {
        [McpServerTool(Name = "missing_description_fixture")]
        public static void MissingDescription(string value)
        {
        }
    }
}
