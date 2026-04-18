using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NamespaceRelocationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static NamespaceRelocationService CreateService()
    {
        return new NamespaceRelocationService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<NamespaceRelocationService>.Instance);
    }

    /// <summary>
    /// Validation case from the plan: break a deliberate namespace cycle by moving
    /// <c>DevicePlatform</c> from <c>Child.Sub</c> to <c>Child</c>. Expectations:
    /// <list type="bullet">
    ///   <item><description>Consumers outside the parent namespace (namespace <c>Other</c>) get a new <c>using Child;</c>.</description></item>
    ///   <item><description>Consumers in <c>Child</c> need no change (ambient resolution covers the new location).</description></item>
    ///   <item><description>Consumers in <c>Child.Sub</c> drop <c>using Child.Sub;</c> only when no sibling types remain and ambient resolution covers the new location.</description></item>
    /// </list>
    /// </summary>
    [TestMethod]
    public async Task Preview_Moves_Type_Up_Namespace_Chain_And_Adjusts_Consumers()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Synthesize a small fixture on top of the Sample solution so we exercise the
        // ambient-resolution rules exactly as the plan describes, without depending on the
        // shape of the production SampleLib types.
        var libDir = workspace.GetPath("SampleLib");
        var deviceDir = Path.Combine(libDir, "NsFixture");
        Directory.CreateDirectory(deviceDir);
        var devicePath = Path.Combine(deviceDir, "DevicePlatform.cs");
        await File.WriteAllTextAsync(devicePath,
            "namespace Child.Sub;\n\n" +
            "public sealed class DevicePlatform\n" +
            "{\n" +
            "    public string Name { get; set; } = \"\";\n" +
            "}\n",
            CancellationToken.None);

        // Consumer in Other — outside the Child ancestor chain. Must gain `using Child;`.
        var otherPath = Path.Combine(deviceDir, "OtherConsumer.cs");
        await File.WriteAllTextAsync(otherPath,
            "using Child.Sub;\n\n" +
            "namespace Other;\n\n" +
            "public sealed class OtherConsumer\n" +
            "{\n" +
            "    public DevicePlatform? Value { get; set; }\n" +
            "}\n",
            CancellationToken.None);

        // Consumer in Child — ancestor of old namespace, ancestor-equal to new namespace.
        // Needs no change (DevicePlatform resolves ambiently after the move).
        var childPath = Path.Combine(deviceDir, "ChildConsumer.cs");
        await File.WriteAllTextAsync(childPath,
            "namespace Child;\n\n" +
            "public sealed class ChildConsumer\n" +
            "{\n" +
            "    public DevicePlatform? Value { get; set; }\n" +
            "}\n",
            CancellationToken.None);

        // Consumer in Child.Sub — descendant of new namespace. After the move, DevicePlatform is
        // in Child which is an ancestor of Child.Sub — ambient resolution applies so no `using
        // Child.Sub;` is needed. Since DevicePlatform was the only member of Child.Sub, the
        // relocated sibling namespace has no types left and the using can be dropped.
        var subPath = Path.Combine(deviceDir, "SubConsumer.cs");
        await File.WriteAllTextAsync(subPath,
            "namespace Child.Sub;\n\n" +
            "public sealed class SubConsumer\n" +
            "{\n" +
            "    public DevicePlatform? Value { get; set; }\n" +
            "}\n",
            CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        var preview = await service.PreviewChangeTypeNamespaceAsync(
            wsId,
            typeName: "DevicePlatform",
            fromNamespace: "Child.Sub",
            toNamespace: "Child",
            newFilePath: null,
            CancellationToken.None);

        Assert.IsNotNull(preview);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

        // The preview must contain the namespace rewrite on DevicePlatform.cs.
        var deviceChange = preview.Changes.FirstOrDefault(c =>
            c.FilePath.EndsWith("DevicePlatform.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(deviceChange, "DevicePlatform.cs should be part of the preview.");
        StringAssert.Contains(deviceChange!.UnifiedDiff, "namespace Child");
        Assert.IsTrue(
            deviceChange.UnifiedDiff.Contains("-namespace Child.Sub", StringComparison.Ordinal) ||
            deviceChange.UnifiedDiff.Contains("-namespace Child.Sub;", StringComparison.Ordinal),
            $"Expected removal of old namespace in diff. Diff:\n{deviceChange.UnifiedDiff}");

        // Consumer in `Other` must gain `using Child;`.
        var otherChange = preview.Changes.FirstOrDefault(c =>
            c.FilePath.EndsWith("OtherConsumer.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(otherChange, "OtherConsumer.cs should appear in the preview.");
        StringAssert.Contains(otherChange!.UnifiedDiff, "+using Child;");

        // Consumer in `Child` must NOT appear in the preview (ambient resolution already covers
        // the new location, and the file had no `using Child.Sub;` to remove).
        var childChange = preview.Changes.FirstOrDefault(c =>
            c.FilePath.EndsWith("ChildConsumer.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(
            childChange,
            "ChildConsumer.cs should need no changes — ambient-namespace resolution covers the new location.");
    }

    [TestMethod]
    public async Task Preview_Rejects_Mismatched_Namespaces()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.PreviewChangeTypeNamespaceAsync(
                wsId,
                typeName: "Dog",
                fromNamespace: "Wrong.Namespace",
                toNamespace: "Some.Other",
                newFilePath: null,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Preview_Rejects_Same_Source_And_Destination_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.PreviewChangeTypeNamespaceAsync(
                wsId,
                typeName: "Dog",
                fromNamespace: "SampleLib",
                toNamespace: "SampleLib",
                newFilePath: null,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task Preview_Rejects_Destination_Outside_Project_Sandbox()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Set up a type that CAN be located so we reach the sandbox check.
        var libDir = workspace.GetPath("SampleLib", "NsFixture2");
        Directory.CreateDirectory(libDir);
        await File.WriteAllTextAsync(
            Path.Combine(libDir, "Widget.cs"),
            "namespace Widgets.Source;\npublic sealed class Widget {}\n",
            CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);
        var service = CreateService();

        // An absolute path outside the project directory must be rejected.
        var outsidePath = Path.Combine(Path.GetTempPath(), "Widget.cs");
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            service.PreviewChangeTypeNamespaceAsync(
                wsId,
                typeName: "Widget",
                fromNamespace: "Widgets.Source",
                toNamespace: "Widgets.Target",
                newFilePath: outsidePath,
                CancellationToken.None));
    }
}
