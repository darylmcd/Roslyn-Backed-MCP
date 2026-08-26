using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Slice-scoped ratchet for the method-description diet across the direct-edit,
/// MSBuild-evaluation, pragma-suppression, and composite symbol-refactor tool groups.
/// Deliberately narrower than <see cref="SurfaceCatalogTests"/>'s assembly-wide 2,000-char
/// guard: that constant stays owned by the global ratchet, while these four types are held
/// to a ~200-char capability statement with operational detail living in XML remarks.
/// </summary>
[TestClass]
public sealed class MethodDescriptionDietEditingMsBuildTests
{
    /// <summary>Per-tool ceiling for a capability statement in this slice.</summary>
    private const int _maxDescriptionCharacters = 200;

    /// <summary>
    /// Aggregate ceiling for the slice, per the initiative spec. Measured 3,770 chars across
    /// 11 tools before the diet and 1,770 after. <see cref="_maxDescriptionCharacters"/> is a
    /// per-tool CEILING, not a target: three tools in this slice already stated their capability
    /// in well under 150 chars, so the aggregate is not bounded below by 11 x 200.
    /// </summary>
    private const int _maxAggregateDescriptionCharacters = 1_850;

    private static readonly Type[] _sliceToolTypes =
    [
        typeof(EditTools),
        typeof(MSBuildTools),
        typeof(SuppressionTools),
        typeof(SymbolRefactorTools),
    ];

    [TestMethod]
    public void SliceToolDescriptions_AreCapabilityStatements()
    {
        var violations = EnumerateSliceTools()
            .Where(entry => entry.Description.Length > _maxDescriptionCharacters)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => $"{entry.Name}: {entry.Description.Length} chars")
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Method [Description] for these tools must be <= {_maxDescriptionCharacters} characters " +
            "(what it does plus the one discriminating trigger); move operational detail to XML " +
            "<remarks>. Violations:\n  " + string.Join("\n  ", violations));
    }

    [TestMethod]
    public void SliceToolDescriptions_StayUnderAggregateBudget()
    {
        var entries = EnumerateSliceTools().ToArray();
        var total = entries.Sum(entry => entry.Description.Length);

        Assert.IsTrue(
            entries.Length > 0,
            "Expected the slice types to declare [McpServerTool] methods; reflection found none.");

        Assert.IsTrue(
            total <= _maxAggregateDescriptionCharacters,
            $"Aggregate method-description budget for the slice is " +
            $"{_maxAggregateDescriptionCharacters} chars across {entries.Length} tools; measured {total}.");
    }

    [TestMethod]
    public void TrimmedDescriptions_KeepTheirDiscriminatingTriggers()
    {
        AssertDescriptionContains(
            "apply_text_edit",
            "Prefer a semantic preview/apply Roslyn tool whenever one exists");
        AssertDescriptionContains(
            "apply_text_edit",
            "Revertible via revert_last_apply");
        AssertDescriptionContains(
            "pragma_scope_widen",
            "Refuses when the move would cross a #region/#endregion boundary");
        AssertDescriptionContains(
            "verify_pragma_suppresses",
            "'cosmetic pragma' bugs");
        AssertDescriptionContains(
            "verify_pragma_suppresses",
            "Read-only");
        AssertDescriptionContains(
            "record_field_add_with_satellites_preview",
            "patternDetectionReason");
        AssertDescriptionContains(
            "symbol_refactor_preview",
            "a failure in any step aborts the whole preview");
        AssertDescriptionContains(
            "split_service_with_di_preview",
            "forwarding facade");
        AssertDescriptionContains(
            "split_service_with_di_preview",
            "DI-registration deltas");
        AssertDescriptionContains(
            "evaluate_msbuild_items",
            "(e.g. Compile, PackageReference)");
        AssertDescriptionContains(
            "get_msbuild_properties",
            "always pass a propertyNameFilter substring or an includedNames allowlist");
    }

    private static void AssertDescriptionContains(string toolName, string expected)
    {
        var entry = EnumerateSliceTools().SingleOrDefault(candidate => candidate.Name == toolName);

        Assert.IsNotNull(entry.Name, $"Tool '{toolName}' was not found on the slice types.");
        StringAssert.Contains(
            entry.Description,
            expected,
            $"Trimming '{toolName}' dropped its discriminating trigger.");
    }

    private static IEnumerable<(string Name, string Description)> EnumerateSliceTools()
        => _sliceToolTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => new
            {
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(),
            })
            .Where(entry => entry.Tool is not null && entry.Description is not null)
            .Select(entry => (entry.Tool!.Name ?? string.Empty, entry.Description!.Description));
}
