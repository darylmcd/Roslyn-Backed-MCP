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
}
