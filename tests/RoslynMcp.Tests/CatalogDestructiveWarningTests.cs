using System.ComponentModel;
using System.Reflection;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CatalogDestructiveWarningTests
{
    private const string DestructiveMarker = "DESTRUCTIVE";
    private const string PreviewSuffix = "_preview";
    private const string NameRetentionRationale = "kept for API stability";

    // apply-composite-preview-destructive-misnomer: a `_preview` suffix tells agents the tool is a
    // safe read. apply_composite_preview is the one deliberate exception — the suffix names the
    // composite preview token it redeems, and the published name is retained for contract stability.
    // Any other `_preview` tool that writes must be renamed, not added to this allowlist silently.
    private static readonly HashSet<string> MutatingPreviewSuffixAllowlist = new(StringComparer.Ordinal)
    {
        "apply_composite_preview",
    };

    [TestMethod]
    public void Catalog_PreviewSuffixedTools_AreReadOnlyAndNonDestructive_ExceptAllowlist()
    {
        var violations = ServerSurfaceCatalog.Tools
            .Where(t => t.Name.EndsWith(PreviewSuffix, StringComparison.Ordinal))
            .Where(t => !MutatingPreviewSuffixAllowlist.Contains(t.Name))
            .Where(t => !t.ReadOnly || t.Destructive)
            .Select(t => $"{t.Name} (readOnly={t.ReadOnly}, destructive={t.Destructive})")
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            "Tools suffixed '_preview' must be read-only and non-destructive; rename mutating tools instead. Violations: "
            + string.Join(", ", violations));
    }

    [TestMethod]
    public void Allowlisted_MutatingPreviewTools_AreDestructiveAndStateNameRetentionRationale()
    {
        foreach (var name in MutatingPreviewSuffixAllowlist)
        {
            var entry = ServerSurfaceCatalog.Tools.SingleOrDefault(t => t.Name == name);
            Assert.IsNotNull(entry, $"Allowlisted tool '{name}' must be present in the catalog; drop stale allowlist entries.");
            Assert.IsFalse(entry.ReadOnly, $"Allowlisted tool '{name}' is only allowlisted because it mutates; catalog must say readOnly=false.");
            Assert.IsTrue(entry.Destructive, $"Allowlisted tool '{name}' must be classified destructive in the catalog.");
            StringAssert.Contains(entry.Summary, NameRetentionRationale,
                $"Catalog summary for '{name}' must state why the '_preview' suffix is retained. Actual: '{entry.Summary}'");
        }

        var method = typeof(OrchestrationTools).GetMethod(
            nameof(OrchestrationTools.ApplyCompositePreview),
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "OrchestrationTools.ApplyCompositePreview must exist.");

        var tool = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.IsNotNull(tool, "ApplyCompositePreview must carry [McpServerTool].");
        Assert.AreEqual("apply_composite_preview", tool.Name);
        Assert.IsFalse(tool.ReadOnly, "[McpServerTool] must declare ReadOnly=false for apply_composite_preview.");
        Assert.IsTrue(tool.Destructive, "[McpServerTool] must declare Destructive=true for apply_composite_preview.");

        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        Assert.IsNotNull(description, "ApplyCompositePreview must carry a [Description] attribute.");
        StringAssert.Contains(description.Description, NameRetentionRationale,
            $"Tool [Description] must state why the '_preview' suffix is retained. Actual: '{description.Description}'");
    }

    [TestMethod]
    public void Catalog_ApplyCompositePreview_SummaryLeadsWithDestructiveMarker()
    {
        var entry = ServerSurfaceCatalog.Tools.SingleOrDefault(t => t.Name == "apply_composite_preview");
        Assert.IsNotNull(entry, "apply_composite_preview must be present in the tool catalog.");
        Assert.IsTrue(entry.Destructive, "Catalog entry must classify the tool as destructive.");
        Assert.IsTrue(
            entry.Summary.StartsWith(DestructiveMarker, StringComparison.Ordinal),
            $"Catalog summary must lead with '{DestructiveMarker}' so agents reading discover_capabilities see the warning before invoking. Actual: '{entry.Summary}'");
    }

    [TestMethod]
    public void Tool_ApplyCompositePreview_DescriptionLeadsWithDestructiveMarker()
    {
        var method = typeof(OrchestrationTools).GetMethod(
            nameof(OrchestrationTools.ApplyCompositePreview),
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "OrchestrationTools.ApplyCompositePreview must exist.");

        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        Assert.IsNotNull(description, "ApplyCompositePreview must carry a [Description] attribute for tool-schema rendering.");
        Assert.IsTrue(
            description.Description.StartsWith(DestructiveMarker, StringComparison.Ordinal),
            $"Tool [Description] must lead with '{DestructiveMarker}' to mirror the catalog summary. Actual: '{description.Description}'");
    }

    // revert-last-apply-single-slot-doc-warning (2026-05-31 surface-test): revert_last_apply is
    // single-slot LIFO — it reverts only the most recent apply, then reports "No operation to
    // revert" even when earlier applies remain in workspace_changes. The [Description] must state
    // that loudly and cross-point to revert_apply_by_sequence as the multi-step path so callers
    // don't assume repeated reverts walk the whole history.
    [TestMethod]
    public void RevertLastApply_Description_StatesSingleSlotLifoAndCrossPointer()
    {
        var method = typeof(UndoTools).GetMethod(
            nameof(UndoTools.RevertLastApply),
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "UndoTools.RevertLastApply must exist.");

        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        Assert.IsNotNull(description, "RevertLastApply must carry a [Description] attribute for tool-schema rendering.");

        var text = description.Description;
        StringAssert.Contains(text, "SINGLE-SLOT LIFO",
            $"Description must state the single-slot LIFO behaviour loudly. Actual: '{text}'");
        StringAssert.Contains(text, "No operation to revert",
            $"Description must warn that a second revert reports 'No operation to revert'. Actual: '{text}'");
        StringAssert.Contains(text, "revert_apply_by_sequence",
            $"Description must cross-point to revert_apply_by_sequence as the multi-step path. Actual: '{text}'");
    }
}
