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

    /// <summary>
    /// Regression: a consumer file whose namespace is an <em>ancestor</em> of the destination
    /// namespace (e.g. consumer in <c>A.B</c>, type moved into <c>A.B.Child</c>) is NOT covered
    /// by ambient resolution — C# lookup climbs ancestors, it does not descend. The preview must
    /// add <c>using A.B.Child;</c> on that consumer file. Closes gh #749.
    /// </summary>
    [TestMethod]
    public async Task Preview_Adds_Using_On_Ancestor_Consumer_When_Type_Moves_Into_Descendant_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var libDir = workspace.GetPath("SampleLib");
        var fixtureDir = Path.Combine(libDir, "AncestorAmbientFixture");
        Directory.CreateDirectory(fixtureDir);

        // Type lives in A.B.Sub initially and will move to A.B.Child.
        var typePath = Path.Combine(fixtureDir, "TypeX.cs");
        await File.WriteAllTextAsync(typePath,
            "namespace A.B.Sub;\n\n" +
            "public sealed class TypeX\n" +
            "{\n" +
            "    public string Name { get; set; } = \"\";\n" +
            "}\n",
            CancellationToken.None);

        // Consumer file is in A.B — an ANCESTOR of the destination A.B.Child. C# ambient lookup
        // only climbs ancestors, so A.B cannot resolve A.B.Child symbols without a using. The
        // preview must add `using A.B.Child;` here.
        var consumerPath = Path.Combine(fixtureDir, "AncestorConsumer.cs");
        await File.WriteAllTextAsync(consumerPath,
            "using A.B.Sub;\n\n" +
            "namespace A.B;\n\n" +
            "public sealed class AncestorConsumer\n" +
            "{\n" +
            "    public TypeX? Value { get; set; }\n" +
            "}\n",
            CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        var preview = await service.PreviewChangeTypeNamespaceAsync(
            wsId,
            typeName: "TypeX",
            fromNamespace: "A.B.Sub",
            toNamespace: "A.B.Child",
            newFilePath: null,
            CancellationToken.None);

        Assert.IsNotNull(preview);
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));

        var consumerChange = preview.Changes.FirstOrDefault(c =>
            c.FilePath.EndsWith("AncestorConsumer.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(
            consumerChange,
            "AncestorConsumer.cs MUST appear in the preview — the ancestor consumer needs `using A.B.Child;`.");
        StringAssert.Contains(
            consumerChange!.UnifiedDiff,
            "+using A.B.Child;",
            $"Expected `+using A.B.Child;` in consumer diff (regression: ancestor-consumer ambient mis-classification). Diff:\n{consumerChange.UnifiedDiff}");
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

    /// <summary>
    /// Perf-refactor regression (refactor-services-full-solution-scan-perf): the SymbolFinder-based
    /// <c>FindTypeInNamespaceAsync</c> must stay namespace-scoped. Two types share the short name
    /// <c>Gadget</c> in different namespaces; relocating from one namespace must resolve uniquely to
    /// that one type (the same-named type in the other namespace must NOT count as a duplicate
    /// match), exactly as the previous per-document syntax walk did.
    /// </summary>
    [TestMethod]
    public async Task Preview_Resolves_Uniquely_When_Same_Short_Name_Exists_In_Another_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixtureDir = Path.Combine(workspace.GetPath("SampleLib"), "ScopedNameFixture");
        Directory.CreateDirectory(fixtureDir);

        // Gadget in Scope.Alpha — the one we relocate.
        await File.WriteAllTextAsync(
            Path.Combine(fixtureDir, "AlphaGadget.cs"),
            "namespace Scope.Alpha;\n\npublic sealed class Gadget\n{\n    public string Name { get; set; } = \"\";\n}\n",
            CancellationToken.None);

        // Gadget in Scope.Beta — a same-named type in a different namespace that must be ignored.
        await File.WriteAllTextAsync(
            Path.Combine(fixtureDir, "BetaGadget.cs"),
            "namespace Scope.Beta;\n\npublic sealed class Gadget\n{\n    public int Value { get; set; }\n}\n",
            CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        var preview = await service.PreviewChangeTypeNamespaceAsync(
            wsId,
            typeName: "Gadget",
            fromNamespace: "Scope.Alpha",
            toNamespace: "Scope.Relocated",
            newFilePath: null,
            CancellationToken.None);

        Assert.IsNotNull(preview);
        // Only the Scope.Alpha declaration must be rewritten; the Scope.Beta Gadget stays untouched.
        var alphaChange = preview.Changes.FirstOrDefault(c =>
            c.FilePath.EndsWith("AlphaGadget.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(alphaChange, "AlphaGadget.cs should be part of the preview.");
        var betaChange = preview.Changes.FirstOrDefault(c =>
            c.FilePath.EndsWith("BetaGadget.cs", StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(betaChange, "BetaGadget.cs (Scope.Beta.Gadget) must NOT be touched — namespace scoping resolves the match uniquely.");
    }

    /// <summary>
    /// Perf-refactor regression (refactor-services-full-solution-scan-perf): a genuinely ambiguous
    /// match — a <c>partial</c> type split across two files in the same namespace surfaces as one
    /// symbol with multiple declaring references — must still throw the ">1 match" ambiguity error,
    /// preserving the exact behaviour of the previous per-declaration walk.
    /// </summary>
    [TestMethod]
    public async Task Preview_Throws_On_Ambiguous_Match_For_Partial_Type_Split_Across_Files()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixtureDir = Path.Combine(workspace.GetPath("SampleLib"), "PartialAmbiguityFixture");
        Directory.CreateDirectory(fixtureDir);

        await File.WriteAllTextAsync(
            Path.Combine(fixtureDir, "TwinPartA.cs"),
            "namespace Dup;\n\npublic partial class Twin\n{\n    public string A { get; set; } = \"\";\n}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(fixtureDir, "TwinPartB.cs"),
            "namespace Dup;\n\npublic partial class Twin\n{\n    public string B { get; set; } = \"\";\n}\n",
            CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.PreviewChangeTypeNamespaceAsync(
                wsId,
                typeName: "Twin",
                fromNamespace: "Dup",
                toNamespace: "Dup.Moved",
                newFilePath: null,
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "matched multiple files");
    }

    /// <summary>
    /// Perf-refactor regression (refactor-services-full-solution-scan-perf): the compilation-symbol
    /// based <c>CountSiblingTypesInNamespaceAsync</c> must still count sibling types that live in a
    /// DIFFERENT project sharing the same namespace. Alpha (SampleLib) moves out of <c>Shared</c>
    /// while Beta (SampleApp) remains in <c>Shared</c>; because a sibling remains, a non-ambient
    /// consumer's <c>using Shared;</c> must be retained and a warning emitted. If the cross-project
    /// sibling were missed, the using would be dropped and no warning would appear.
    /// </summary>
    [TestMethod]
    public async Task Preview_Counts_Cross_Project_Sibling_Types_And_Keeps_Using()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        // Alpha lives in SampleLib under namespace Shared — this is the type we relocate.
        var libFixtureDir = Path.Combine(workspace.GetPath("SampleLib"), "CrossProjectSiblingFixture");
        Directory.CreateDirectory(libFixtureDir);
        await File.WriteAllTextAsync(
            Path.Combine(libFixtureDir, "Alpha.cs"),
            "namespace Shared;\n\npublic sealed class Alpha\n{\n    public string Name { get; set; } = \"\";\n}\n",
            CancellationToken.None);

        // Beta lives in SampleApp under the SAME namespace Shared — a cross-project sibling that
        // must keep `Shared` populated after Alpha moves out.
        var appFixtureDir = Path.Combine(workspace.GetPath("SampleApp"), "CrossProjectSiblingFixture");
        Directory.CreateDirectory(appFixtureDir);
        await File.WriteAllTextAsync(
            Path.Combine(appFixtureDir, "Beta.cs"),
            "namespace Shared;\n\npublic sealed class Beta\n{\n    public int Value { get; set; }\n}\n",
            CancellationToken.None);

        // Consumer (SampleApp, references SampleLib) in a non-ambient namespace with `using Shared;`
        // referencing Alpha.
        await File.WriteAllTextAsync(
            Path.Combine(appFixtureDir, "AlphaConsumer.cs"),
            "using Shared;\n\nnamespace Consumers;\n\npublic sealed class AlphaConsumer\n{\n    public Alpha? Value { get; set; }\n}\n",
            CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        var preview = await service.PreviewChangeTypeNamespaceAsync(
            wsId,
            typeName: "Alpha",
            fromNamespace: "Shared",
            toNamespace: "Shared.Moved",
            newFilePath: null,
            CancellationToken.None);

        Assert.IsNotNull(preview);
        Assert.IsNotNull(
            preview.Warnings,
            "A warning must be emitted because a cross-project sibling (Beta) remains in `Shared`, so `using Shared;` is kept.");
        Assert.IsTrue(
            preview.Warnings!.Any(w => w.Contains("Kept `using Shared;`", StringComparison.Ordinal)),
            $"Expected a kept-using warning naming `Shared`. Warnings:\n{string.Join("\n", preview.Warnings)}");
    }
}
