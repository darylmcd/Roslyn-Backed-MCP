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

    // apply-composite-preview-destructive-misnomer: a `_preview` suffix tells agents the tool is a
    // safe read. The misnamed destructive alias apply_composite_preview was removed at 5.0.0, so no
    // `_preview` tool may write: rename a mutating tool instead of adding an exception here.
    [TestMethod]
    public void Catalog_PreviewSuffixedTools_AreReadOnlyAndNonDestructive()
    {
        var violations = ServerSurfaceCatalog.Tools
            .Where(t => t.Name.EndsWith(PreviewSuffix, StringComparison.Ordinal))
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
    public void Catalog_ApplyComposite_IsDestructiveAndLeadsWithWarning()
    {
        var entry = ServerSurfaceCatalog.Tools.SingleOrDefault(t => t.Name == "apply_composite");
        Assert.IsNotNull(entry, "apply_composite must be present in the tool catalog.");
        Assert.IsFalse(entry.ReadOnly, "apply_composite must be classified as a write operation.");
        Assert.IsTrue(entry.Destructive, "apply_composite must be classified as destructive.");
        Assert.IsTrue(
            entry.Summary.StartsWith(DestructiveMarker, StringComparison.Ordinal),
            $"Catalog summary for apply_composite must lead with '{DestructiveMarker}'. Actual: '{entry.Summary}'");
    }

    [TestMethod]
    public void Tool_ApplyComposite_IsDestructiveAndLeadsWithWarning()
    {
        var method = typeof(OrchestrationTools).GetMethod(
            nameof(OrchestrationTools.ApplyComposite),
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "OrchestrationTools.ApplyComposite must exist.");

        var tool = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.IsNotNull(tool, "ApplyComposite must carry [McpServerTool].");
        Assert.IsFalse(tool.ReadOnly, $"{tool.Name} must declare ReadOnly=false.");
        Assert.IsTrue(tool.Destructive, $"{tool.Name} must declare Destructive=true.");

        var description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        Assert.IsNotNull(description, "ApplyComposite must carry a [Description] attribute.");
        Assert.IsTrue(
            description.Description.StartsWith(DestructiveMarker, StringComparison.Ordinal),
            $"Tool [Description] for {tool.Name} must lead with '{DestructiveMarker}'. Actual: '{description.Description}'");
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
