using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

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

    /// <summary>
    /// Regression (change-type-namespace-emits-invalid-syntax): when the relocated type stays in a
    /// file that keeps sibling types, the appended namespace declaration was emitted trivia-free
    /// (<c>namespaceShapes.Moved{</c>) and, for a file-scoped source, mixed a block namespace with
    /// the file-scoped one (CS8955). Both source styles must now produce a file that compiles
    /// with the relocated type in the destination namespace.
    /// </summary>
    [TestMethod]
    [DataRow(true, DisplayName = "file-scoped source namespace")]
    [DataRow(false, DisplayName = "block-scoped source namespace")]
    public async Task Preview_Keeps_Siblings_In_Same_File_And_Emits_Compilable_Namespace_Syntax(bool fileScoped)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixtureDir = Path.Combine(workspace.GetPath("SampleLib"), "SameFileSiblingFixture");
        Directory.CreateDirectory(fixtureDir);
        var shapesPath = Path.Combine(fixtureDir, "Shapes.cs");
        var source = fileScoped
            ? "namespace Shapes.Source;\n\n" +
              "public sealed class Circle\n" +
              "{\n" +
              "    public double Radius { get; set; }\n" +
              "}\n\n" +
              "/// <summary>Square shape.</summary>\n" +
              "public sealed class Square\n" +
              "{\n" +
              "    public double Side { get; set; }\n" +
              "}\n"
            : "namespace Shapes.Source\n" +
              "{\n" +
              "    public sealed class Circle\n" +
              "    {\n" +
              "        public double Radius { get; set; }\n" +
              "    }\n\n" +
              "    /// <summary>Square shape.</summary>\n" +
              "    public sealed class Square\n" +
              "    {\n" +
              "        public double Side { get; set; }\n" +
              "    }\n" +
              "}\n";
        await File.WriteAllTextAsync(shapesPath, source, CancellationToken.None);

        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        var preview = await service.PreviewChangeTypeNamespaceAsync(
            wsId,
            typeName: "Square",
            fromNamespace: "Shapes.Source",
            toNamespace: "Shapes.Moved",
            newFilePath: null,
            CancellationToken.None);

        var retrieved = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(retrieved, "the preview token must be redeemable immediately after the preview");
        var document = retrieved.Value.ModifiedSolution.Projects
            .SelectMany(p => p.Documents)
            .Single(d => d.FilePath is not null && d.FilePath.EndsWith(
                Path.Combine("SameFileSiblingFixture", "Shapes.cs"), StringComparison.OrdinalIgnoreCase));
        var text = (await document.GetTextAsync(CancellationToken.None)).ToString();

        Assert.IsFalse(text.Contains("namespaceShapes", StringComparison.Ordinal), $"Keyword glued to name:\n{text}");
        Assert.IsFalse(text.Contains("Moved{", StringComparison.Ordinal), $"Name glued to brace:\n{text}");

        var compilation = await document.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var tree = await document.GetSyntaxTreeAsync(CancellationToken.None);
        var errors = compilation.GetDiagnostics(CancellationToken.None)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Location.SourceTree == tree)
            .ToList();
        Assert.AreEqual(0, errors.Count, $"Rewritten file must compile. Errors:\n{string.Join("\n", errors)}\n{text}");

        Assert.IsNotNull(compilation.GetTypeByMetadataName("Shapes.Moved.Square"), $"Square must move to Shapes.Moved:\n{text}");
        Assert.IsNotNull(compilation.GetTypeByMetadataName("Shapes.Source.Circle"), $"Circle must stay in Shapes.Source:\n{text}");
        Assert.IsNull(compilation.GetTypeByMetadataName("Shapes.Source.Square"), $"Square must leave Shapes.Source:\n{text}");
        StringAssert.Contains(text, "        public double Side { get; set; }", $"Relocated member must be indented inside the block namespace:\n{text}");
        StringAssert.Contains(text, "    /// <summary>Square shape.</summary>", $"Relocated type must keep its doc comment:\n{text}");
        Assert.AreEqual(
            text.IndexOf("Square shape.", StringComparison.Ordinal),
            text.LastIndexOf("Square shape.", StringComparison.Ordinal),
            $"The doc comment must move with the type, not be duplicated:\n{text}");
        Assert.IsTrue(text.EndsWith('\n'), $"Rewritten file must end with a newline:\n{text}");
    }

    [TestMethod]
    public async Task Preview_Rejects_Mismatched_Namespaces()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.PreviewChangeTypeNamespaceAsync(
                wsId,
                typeName: "Dog",
                fromNamespace: "Wrong.Namespace",
                toNamespace: "Some.Other",
                newFilePath: null,
                CancellationToken.None));
    }

    /// <summary>
    /// Regression (relocation-selected-enum-delegate-reported-not-found): selecting an enum or a
    /// delegate matched by name and namespace but was skipped for not being a
    /// <c>TypeDeclarationSyntax</c>, so the caller was told the type "was not found".
    /// </summary>
    [TestMethod]
    [DataRow("public enum Selected { A }", "Enum", DisplayName = "enum")]
    [DataRow("public delegate void Selected();", "Delegate", DisplayName = "delegate")]
    public async Task Preview_Names_The_Unsupported_Kind_For_An_Enum_Or_Delegate_Selection(string declaration, string kind)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixtureDir = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory);
        Directory.CreateDirectory(fixtureDir);
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "Source.cs"),
            "namespace Old\n{\n    " + declaration + "\n\n    public class Keep { }\n}\n", CancellationToken.None);
        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            CreateService().PreviewChangeTypeNamespaceAsync(
                wsId, "Selected", "Old", "New", newFilePath: null, CancellationToken.None));

        StringAssert.Contains(ex.Message, $"type-kind {kind} not supported");
        StringAssert.Contains(ex.Message, "change_type_namespace_preview");
        Assert.IsFalse(ex.Message.Contains("not found", StringComparison.Ordinal),
            $"{kind} must be reported as an unsupported kind, not 'not found': {ex.Message}");
    }

    /// <summary>
    /// A genuine miss still reads "not found": an enum in a DIFFERENT namespace must not turn the
    /// refusal into an unsupported-kind error.
    /// </summary>
    [TestMethod]
    public async Task Preview_Reports_Not_Found_When_The_Enum_Lives_In_Another_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixtureDir = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory);
        Directory.CreateDirectory(fixtureDir);
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "Source.cs"),
            "namespace Elsewhere\n{\n    public enum Selected { A }\n}\n", CancellationToken.None);
        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            CreateService().PreviewChangeTypeNamespaceAsync(
                wsId, "Selected", "Old", "New", newFilePath: null, CancellationToken.None));

        StringAssert.Contains(ex.Message, "was not found in the workspace");
        Assert.IsFalse(ex.Message.Contains("type-kind", StringComparison.Ordinal), ex.Message);
    }

    [TestMethod]
    public async Task Preview_Rejects_Same_Source_And_Destination_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var wsId = await workspace.LoadAsync(CancellationToken.None);

        var service = CreateService();
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            service.PreviewChangeTypeNamespaceAsync(
                wsId,
                typeName: "Dog",
                fromNamespace: "SampleLib",
                toNamespace: "SampleLib",
                newFilePath: null,
                CancellationToken.None));
    }

    [TestMethod]
    [DataRow("prefix")]
    [DataRow("parent")]
    [DataRow("root")]
    public async Task Preview_Rejects_Destination_Outside_Project_Sandbox(string shape)
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
        var outsidePath = shape switch
        {
            "prefix" => workspace.GetPath("SampleLib2", "Widget.cs"),
            "parent" => workspace.GetPath("SampleLib", "..", "Widget.cs"),
            _ => workspace.GetPath("SampleLib")
        };
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
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
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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

    private const string RelocationFixtureDirectory = "RelocationKeepFixture";

    private async Task<(Microsoft.CodeAnalysis.Solution Modified, Microsoft.CodeAnalysis.Document? SourceDocument, string SourcePath, string DestinationPath, RoslynMcp.Core.Models.RefactoringPreviewDto Preview)>
        PreviewRelocationAsync(
            IsolatedWorkspaceScope workspace,
            string source,
            string? consumerSource = null,
            string consumerProject = "SampleLib",
            (string Project, string Source)? otherFile = null,
            bool relocate = true,
            string typeName = "Selected",
            string fromNamespace = "Old",
            string toNamespace = "New")
    {
        var fixtureDir = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory);
        Directory.CreateDirectory(fixtureDir);
        var sourcePath = Path.Combine(fixtureDir, "Source.cs");
        var destinationPath = Path.Combine(fixtureDir, "Moved.cs");
        await File.WriteAllTextAsync(sourcePath, source, CancellationToken.None);
        if (consumerSource is not null)
            await WriteFixtureFileAsync(workspace, consumerProject, "Consumer.cs", consumerSource);
        if (otherFile is { } other)
            await WriteFixtureFileAsync(workspace, other.Project, "OtherDeclaration.cs", other.Source);

        var wsId = await workspace.LoadAsync(CancellationToken.None);
        var preview = await CreateService().PreviewChangeTypeNamespaceAsync(
            wsId, typeName, fromNamespace, toNamespace, relocate ? destinationPath : null, CancellationToken.None);

        var retrieved = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(retrieved, "the preview token must be redeemable immediately after the preview");
        var modified = retrieved.Value.ModifiedSolution;
        var sourceDocument = modified.Projects.SelectMany(p => p.Documents)
            .SingleOrDefault(d => string.Equals(d.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        return (modified, sourceDocument, sourcePath, destinationPath, preview);
    }

    private static async Task WriteFixtureFileAsync(
        IsolatedWorkspaceScope workspace, string project, string fileName, string content)
    {
        var dir = Path.Combine(workspace.GetPath(project), RelocationFixtureDirectory);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), content, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<string>> CompileErrorsInAsync(
        Microsoft.CodeAnalysis.Solution solution, params string[] filePaths)
    {
        var errors = new List<string>();
        foreach (var project in solution.Projects.Where(p => p.Name is "SampleLib" or "SampleApp"))
        {
            var compilation = await project.GetCompilationAsync(CancellationToken.None);
            Assert.IsNotNull(compilation);
            foreach (var diagnostic in compilation.GetDiagnostics(CancellationToken.None))
            {
                if (diagnostic.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error) continue;
                var path = diagnostic.Location.SourceTree?.FilePath;
                if (path is not null && filePaths.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
                    errors.Add(diagnostic.ToString());
            }
        }
        return errors;
    }

    /// <summary>
    /// Regression (relocation-source-delete-preserves-other-members): the old delete predicate
    /// counted only <c>TypeDeclarationSyntax</c> members, so an enum, a delegate, a declaration in
    /// another or nested namespace block, a global-namespace type, an assembly attribute or
    /// <c>#if</c>-disabled text left behind in the source file was deleted with it. Each shape keeps
    /// exactly one extra declaration (<c>Keep</c>); <c>expectedMetadataName</c> is null where the
    /// declaration is disabled text and therefore not part of the compilation.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public enum Keep { A }\n}\n", "Old.Keep", "enum Keep",
        DisplayName = "block namespace + enum")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public delegate void Keep();\n}\n", "Old.Keep", "delegate void Keep",
        DisplayName = "block namespace + delegate")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n\npublic enum Keep { A }\n", "Old.Keep", "enum Keep",
        DisplayName = "file-scoped namespace + enum")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n\npublic delegate void Keep();\n", "Old.Keep", "delegate void Keep",
        DisplayName = "file-scoped namespace + delegate")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n\nnamespace Elsewhere\n{\n    public enum Keep { A }\n}\n", "Elsewhere.Keep", "enum Keep",
        DisplayName = "enum in a sibling namespace block")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    namespace Inner\n    {\n        public enum Keep { A }\n    }\n}\n", "Old.Inner.Keep", "enum Keep",
        DisplayName = "enum in a nested namespace")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n\npublic enum Keep { A }\n", "Keep", "enum Keep",
        DisplayName = "enum in the global namespace")]
    [DataRow("[assembly: System.Reflection.AssemblyMetadata(\"keep\", \"1\")]\n\nnamespace Old\n{\n    public class Selected { }\n}\n", null, "AssemblyMetadata",
        DisplayName = "assembly attribute")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n#if NEVER\n    public enum Keep { A }\n#endif\n}\n", null, "enum Keep",
        DisplayName = "disabled declaration after the selected type")]
    public async Task Preview_Relocation_Keeps_Source_File_When_Other_Declarations_Remain(
        string source, string? expectedMetadataName, string expectedText)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, sourcePath, destinationPath, _) = await PreviewRelocationAsync(workspace, source);

        Assert.IsNotNull(sourceDocument, "the source file still declares 'Keep' and must survive the relocation");
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(text, expectedText, $"The retained source file lost its other declaration:\n{text}");
        Assert.IsFalse(text.Contains("Selected", StringComparison.Ordinal), $"The relocated type must leave the source file:\n{text}");

        var errors = await CompileErrorsInAsync(modified, sourcePath, destinationPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");

        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("New.Selected"), "Selected must arrive in New.");
        Assert.IsNull(compilation.GetTypeByMetadataName("Old.Selected"), "Selected must leave Old.");
        if (expectedMetadataName is not null)
            Assert.IsNotNull(compilation.GetTypeByMetadataName(expectedMetadataName), $"'{expectedMetadataName}' must still exist:\n{text}");
    }

    /// <summary>
    /// Controls for the keep test: when nothing but namespace shells, usings and trivia is left,
    /// the source file is still removed. The second row guards against over-retention: a
    /// <c>#if</c>-disabled region inside the removed type's own body leaves no content behind.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n", DisplayName = "sole type, block namespace")]
    [DataRow("using System;\n\nnamespace Old;\n\npublic class Selected { }\n", DisplayName = "sole type, file-scoped namespace with a using")]
    [DataRow("namespace Old\n{\n    public class Selected\n    {\n#if NEVER\n        public int Hidden;\n#endif\n    }\n}\n", DisplayName = "sole type with a disabled region inside its body")]
    public async Task Preview_Relocation_Deletes_Source_File_When_Nothing_Remains(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, _, destinationPath, _) = await PreviewRelocationAsync(workspace, source);

        Assert.IsNull(sourceDocument, "the source file has no remaining declaration and must be removed");
        var errors = await CompileErrorsInAsync(modified, destinationPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Disabled text directly above the selected type is that type's leading trivia, so it travels
    /// with the declaration into the new file (nothing is lost) and nothing remains in the source
    /// file. Pins the placement half of the disabled-declaration shape that the keep test covers
    /// for text after the selected type.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n#if NEVER\n    public enum Keep { A }\n#endif\n    public class Selected { }\n}\n", DisplayName = "block namespace")]
    [DataRow("namespace Old;\n\n#if NEVER\npublic enum Keep { A }\n#endif\npublic class Selected { }\n", DisplayName = "file-scoped namespace")]
    public async Task Preview_Relocation_Moves_Disabled_Text_Above_The_Selected_Type_With_It(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, _, destinationPath, _) = await PreviewRelocationAsync(workspace, source);

        Assert.IsNull(sourceDocument, "the disabled text left the source file with the selected type, so nothing remains");
        var destination = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, destinationPath, StringComparison.OrdinalIgnoreCase));
        var destinationText = (await destination.GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(destinationText, "enum Keep", $"The disabled declaration must not be lost:\n{destinationText}");
    }

    /// <summary>
    /// The retained source file and the consumer rewrite must agree: while <c>Keep</c> still lives in
    /// <c>Old</c>, a consumer that imports <c>Old</c> keeps <c>using Old;</c> (with the kept-using
    /// warning) so the previewed solution compiles. Counting declarations in OTHER documents and
    /// projects stays with <c>relocation-consumer-using-counts-enum-delegate-members</c>.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public enum Keep { A }\n}\n", DisplayName = "enum in the same namespace block")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n\nnamespace Old\n{\n    public delegate void Keep();\n}\n", DisplayName = "delegate in a second block of the same namespace")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n\nnamespace Old\n{\n    public class Keep { }\n}\n", DisplayName = "class in a second block of the same namespace")]
    public async Task Preview_Relocation_Keeps_Old_Using_In_Consumers_While_The_Source_File_Still_Declares_Old_Members(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string consumer =
            "using Old;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n\n    public Keep? Other { get; set; }\n}\n";

        var (modified, sourceDocument, sourcePath, destinationPath, preview) =
            await PreviewRelocationAsync(workspace, source, consumer);

        Assert.IsNotNull(sourceDocument, "the source file still declares 'Keep' and must survive");
        var consumerPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Consumer.cs");
        var consumerDocument = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, consumerPath, StringComparison.OrdinalIgnoreCase));
        var consumerText = (await consumerDocument.GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(consumerText, "using Old;", $"The consumer still needs `using Old;` for Keep:\n{consumerText}");
        StringAssert.Contains(consumerText, "using New;", $"The consumer needs `using New;` for Selected:\n{consumerText}");
        Assert.IsNotNull(preview.Warnings);
        Assert.IsTrue(
            preview.Warnings!.Any(w => w.Contains("Kept `using Old;`", StringComparison.Ordinal)),
            $"Expected a kept-using warning naming `Old`. Warnings:\n{string.Join("\n", preview.Warnings)}");

        var errors = await CompileErrorsInAsync(modified, sourcePath, destinationPath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Regression (relocation-consumer-using-counts-enum-delegate-members): the cross-document count
    /// only recognized <c>TypeDeclarationSyntax</c>, so an enum or delegate left in <c>Old</c> by
    /// ANOTHER file or project was invisible and consumers lost the <c>using Old;</c> they still
    /// need. The source file is deleted by the relocation, so only the cross-document count can see
    /// <c>Keep</c>.
    /// </summary>
    [TestMethod]
    [DataRow("SampleLib", "public enum Keep { A }", DisplayName = "enum in another file of the same project")]
    [DataRow("SampleLib", "public delegate void Keep();", DisplayName = "delegate in another file of the same project")]
    [DataRow("SampleApp", "public enum Keep { A }", DisplayName = "enum in another project")]
    [DataRow("SampleApp", "public delegate void Keep();", DisplayName = "delegate in another project")]
    public async Task Preview_Relocation_Keeps_Old_Using_When_Enum_Or_Delegate_Remains_In_Another_Document(
        string otherProject, string keepDeclaration)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string source = "namespace Old\n{\n    public class Selected { }\n}\n";
        var other = $"namespace Old\n{{\n    {keepDeclaration}\n}}\n";
        // SampleApp references SampleLib, so a consumer there sees both projects' declarations.
        const string consumer =
            "using Old;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n\n    public Keep? Other { get; set; }\n}\n";
        var consumerProject = otherProject == "SampleApp" ? "SampleApp" : "SampleLib";

        var (modified, _, sourcePath, destinationPath, preview) = await PreviewRelocationAsync(
            workspace, source, consumer, consumerProject, (otherProject, other));

        var consumerPath = Path.Combine(workspace.GetPath(consumerProject), RelocationFixtureDirectory, "Consumer.cs");
        await AssertConsumerKeepsOldAndGainsNewAsync(modified, consumerPath, preview);
        var errors = await CompileErrorsInAsync(modified, sourcePath, destinationPath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Non-relocating change (no <c>newFilePath</c>): another <c>namespace Old</c> block of the
    /// SOURCE file keeps <c>Old</c> populated, but the source document is excluded from the
    /// cross-document count and only the containing block was inspected, so the consumer lost
    /// <c>using Old;</c>.
    /// </summary>
    [TestMethod]
    [DataRow("public class Keep { }", DisplayName = "class in a second block")]
    [DataRow("public enum Keep { A }", DisplayName = "enum in a second block")]
    [DataRow("public delegate void Keep();", DisplayName = "delegate in a second block")]
    public async Task Preview_InPlace_Namespace_Change_Keeps_Old_Using_When_A_Second_Block_Remains(string keepDeclaration)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var source = $"namespace Old\n{{\n    public class Selected {{ }}\n}}\n\nnamespace Old\n{{\n    {keepDeclaration}\n}}\n";
        const string consumer =
            "using Old;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n\n    public Keep? Other { get; set; }\n}\n";

        var (modified, _, sourcePath, _, preview) = await PreviewRelocationAsync(
            workspace, source, consumer, relocate: false);

        var consumerPath = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory, "Consumer.cs");
        await AssertConsumerKeepsOldAndGainsNewAsync(modified, consumerPath, preview);
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// Regression (relocation-same-file-rewrite-preserves-other-members): the in-place decision
    /// counted only <c>TypeDeclarationSyntax</c> siblings, so a same-block enum, delegate or nested
    /// namespace was renamed along with the whole containing declaration and moved to <c>New</c>.
    /// Only <c>Selected</c> may move; the sibling stays in <c>Old</c> (nested: <c>Old.Inner</c>).
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public enum Keep { A }\n}\n", "Old.Keep", "enum Keep",
        DisplayName = "block namespace + enum")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public delegate void Keep();\n}\n", "Old.Keep", "delegate void Keep",
        DisplayName = "block namespace + delegate")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n\npublic enum Keep { A }\n", "Old.Keep", "enum Keep",
        DisplayName = "file-scoped namespace + enum")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n\npublic delegate void Keep();\n", "Old.Keep", "delegate void Keep",
        DisplayName = "file-scoped namespace + delegate")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    namespace Inner\n    {\n        public enum Keep { A }\n    }\n}\n", "Old.Inner.Keep", "enum Keep",
        DisplayName = "block namespace + nested namespace with an enum")]
    [DataRow("namespace Old\n{\n    using System;\n\n    public class Selected { }\n\n    public enum Keep { A }\n}\n", "Old.Keep", "enum Keep",
        DisplayName = "block namespace with a using + enum")]
    public async Task Preview_InPlace_Namespace_Change_Keeps_NonClass_Members_In_Old(
        string source, string keepMetadataName, string expectedText)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, sourcePath, _, _) = await PreviewRelocationAsync(workspace, source, relocate: false);

        Assert.IsNotNull(sourceDocument, "an in-place namespace change keeps the source file");
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(text, expectedText, $"The sibling declaration was lost:\n{text}");

        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("New.Selected"), $"Selected must arrive in New:\n{text}");
        Assert.IsNull(compilation.GetTypeByMetadataName("Old.Selected"), $"Selected must leave Old:\n{text}");
        Assert.IsNotNull(compilation.GetTypeByMetadataName(keepMetadataName),
            $"'{keepMetadataName}' must stay in Old (it was dragged along with the namespace rename):\n{text}");
        Assert.IsNull(compilation.GetTypeByMetadataName(keepMetadataName.Replace("Old", "New", StringComparison.Ordinal)),
            $"The sibling must not appear in New:\n{text}");

        if (source.Contains("using System;", StringComparison.Ordinal))
        {
            // The using sits inside the namespace block, which stays behind with the sibling.
            var namespaceIndex = text.IndexOf("namespace Old", StringComparison.Ordinal);
            var usingIndex = text.IndexOf("using System;", StringComparison.Ordinal);
            var keepIndex = text.IndexOf("enum Keep", StringComparison.Ordinal);
            Assert.IsTrue(namespaceIndex >= 0 && namespaceIndex < usingIndex && usingIndex < keepIndex,
                $"The block-level using must stay with the retained block:\n{text}");
        }

        var errors = await CompileErrorsInAsync(modified, sourcePath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Controls for the keep test: a sole type still renames its declaration in place, including a
    /// block-level <c>using</c> (namespace usings are not members, so they never force extraction).
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n", DisplayName = "sole type, block namespace")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n", DisplayName = "sole type, file-scoped namespace")]
    [DataRow("namespace Old\n{\n    using System;\n\n    public class Selected { }\n}\n", DisplayName = "sole type, block namespace with a using")]
    public async Task Preview_InPlace_Namespace_Change_Renames_Declaration_When_Type_Is_Sole_Member(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, sourcePath, _, _) = await PreviewRelocationAsync(workspace, source, relocate: false);

        Assert.IsNotNull(sourceDocument);
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        Assert.IsFalse(text.Contains("namespace Old", StringComparison.Ordinal), $"The declaration must be renamed in place:\n{text}");
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(text, "namespace ").Count,
            $"Exactly one namespace declaration must remain:\n{text}");
        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("New.Selected"));
        Assert.IsNull(compilation.GetTypeByMetadataName("Old.Selected"));
        var errors = await CompileErrorsInAsync(modified, sourcePath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// A consumer that references only the kept enum must keep compiling: the enum stays in
    /// <c>Old</c>, so the consumer is untouched (it does not reference <c>Selected</c>).
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public enum Keep { A }\n}\n", DisplayName = "block namespace + enum")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n\npublic enum Keep { A }\n", DisplayName = "file-scoped namespace + enum")]
    public async Task Preview_InPlace_Namespace_Change_Consumer_Referencing_Only_The_Kept_Enum_Still_Compiles(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string consumer =
            "using Old;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Keep? Value { get; set; }\n}\n";

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace, source, consumer, relocate: false);

        var consumerPath = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory, "Consumer.cs");
        var consumerDocument = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, consumerPath, StringComparison.OrdinalIgnoreCase));
        var consumerText = (await consumerDocument.GetTextAsync(CancellationToken.None)).ToString();
        Assert.IsFalse(consumerText.Contains("using New;", StringComparison.Ordinal),
            $"A consumer that does not reference Selected must not gain `using New;`:\n{consumerText}");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    /// <summary>
    /// A consumer that references both the moved type and the kept enum needs both usings.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public enum Keep { A }\n}\n", DisplayName = "block namespace + enum")]
    [DataRow("namespace Old;\n\npublic class Selected { }\n\npublic delegate void Keep();\n", DisplayName = "file-scoped namespace + delegate")]
    public async Task Preview_InPlace_Namespace_Change_Consumer_Referencing_Both_Keeps_Old_And_Gains_New(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string consumer =
            "using Old;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n\n    public Keep? Other { get; set; }\n}\n";

        var (modified, _, sourcePath, _, preview) = await PreviewRelocationAsync(workspace, source, consumer, relocate: false);

        var consumerPath = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory, "Consumer.cs");
        await AssertConsumerKeepsOldAndGainsNewAsync(modified, consumerPath, preview);
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}");
    }

    private static async Task AssertConsumerKeepsOldAndGainsNewAsync(
        Microsoft.CodeAnalysis.Solution modified, string consumerPath, RoslynMcp.Core.Models.RefactoringPreviewDto preview)
    {
        var consumerDocument = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, consumerPath, StringComparison.OrdinalIgnoreCase));
        var consumerText = (await consumerDocument.GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(consumerText, "using Old;", $"The consumer still needs `using Old;` for Keep:\n{consumerText}");
        StringAssert.Contains(consumerText, "using New;", $"The consumer needs `using New;` for Selected:\n{consumerText}");
        Assert.IsNotNull(preview.Warnings);
        Assert.IsTrue(
            preview.Warnings!.Any(w => w.Contains("Kept `using Old;`", StringComparison.Ordinal)),
            $"Expected a kept-using warning naming `Old`. Warnings:\n{string.Join("\n", preview.Warnings)}");
    }

    /// <summary>
    /// Regression (relocation-same-file-nested-destination-namespace): the in-place branch renamed
    /// only the innermost declaration, so relocating <c>Outer.Inner.Worker</c> to <c>Target</c>
    /// produced <c>Outer.Target.Worker</c>. The destination is the FULL namespace; unrelated
    /// members of the enclosing declaration stay, and the consumer binds to the new location.
    /// </summary>
    [TestMethod]
    public async Task Preview_InPlace_Honors_The_Full_Destination_Namespace_When_The_Type_Is_In_A_Nested_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string source =
            "namespace Outer\n{\n    public class Unrelated { }\n\n    namespace Inner\n    {\n        public class Worker { }\n    }\n}\n";
        const string consumer =
            "using Outer.Inner;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Worker? Value { get; set; }\n}\n";

        var (modified, sourceDocument, sourcePath, _, _) = await PreviewRelocationAsync(
            workspace, source, consumer, relocate: false, typeName: "Worker", fromNamespace: "Outer.Inner", toNamespace: "Target");
        var consumerPath = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory, "Consumer.cs");

        Assert.IsNotNull(sourceDocument);
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("Target.Worker"), $"Worker must arrive in Target:\n{text}");
        Assert.IsNull(compilation.GetTypeByMetadataName("Outer.Target.Worker"), $"The destination must not stay under Outer:\n{text}");
        Assert.IsNull(compilation.GetTypeByMetadataName("Outer.Inner.Worker"), $"Worker must leave Outer.Inner:\n{text}");
        Assert.IsNotNull(compilation.GetTypeByMetadataName("Outer.Unrelated"), $"The unrelated enclosing member must stay:\n{text}");
        var consumerDocument = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, consumerPath, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains((await consumerDocument.GetTextAsync(CancellationToken.None)).ToString(), "using Target;");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Regression (relocation-same-file-directive-ownership): the selected declaration was removed
    /// with the conditional directive that opens in its own trivia (stranding the <c>#else</c> /
    /// <c>#endif</c> that close it), and the new block was appended before the EOF trivia, so a
    /// conditional closing at EOF cut across the artificial namespace boundary. The previewed AND
    /// the applied source must parse and compile under both define sets, every retained
    /// declaration must stay under its own condition, and only the selected type moves.
    /// </summary>
    [TestMethod]
    [DataRow(true, false, DisplayName = "file-scoped, conditional wraps the selected type and closes at EOF")]
    [DataRow(true, true, DisplayName = "file-scoped, conditional wraps the selected type, active sibling after it")]
    [DataRow(false, false, DisplayName = "block, conditional wraps the selected type and closes before the brace")]
    [DataRow(false, true, DisplayName = "block, conditional wraps the selected type, active sibling after it")]
    public async Task Preview_SameFile_Keeps_Conditional_Directive_Pairs_Whole_Under_Both_Defines(bool fileScoped, bool tailSibling)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var indent = fileScoped ? "" : "    ";
        var body =
            $"{indent}public class Keep {{ }}\n\n" +
            "#if !ALT\n" +
            $"{indent}public class Selected {{ }}\n" +
            "#else\n" +
            $"{indent}public class AltKeep {{ }}\n" +
            "#endif\n" +
            (tailSibling ? $"{indent}public class Tail {{ }}\n" : "");
        var source = fileScoped ? "namespace Old;\n\n" + body : "namespace Old\n{\n" + body + "}\n";

        var (modified, sourceDocument, sourcePath, _, preview) = await PreviewRelocationAsync(workspace, source, relocate: false);
        Assert.IsNotNull(sourceDocument);
        var previewText = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        await AssertDirectiveConfigurationsAsync(modified, sourcePath, tailSibling, previewText);

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "same_file_directive_pairs", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var appliedText = await File.ReadAllTextAsync(sourcePath);
        Assert.AreEqual(previewText, appliedText, "the applied file must equal the previewed text");
        await AssertDirectiveConfigurationsAsync(reloaded, sourcePath, tailSibling, appliedText);
    }

    private static async Task AssertDirectiveConfigurationsAsync(
        Microsoft.CodeAnalysis.Solution solution, string sourcePath, bool tailSibling, string text)
    {
        foreach (var defines in new[] { Array.Empty<string>(), new[] { "ALT" } })
        {
            var alt = defines.Length > 0;
            var label = alt ? "ALT defined" : "ALT undefined";
            var compilation = await CompileSampleLibUnderDefinesAsync(solution, defines);
            var errors = compilation.GetDiagnostics(CancellationToken.None)
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error &&
                            string.Equals(d.Location.SourceTree?.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.ToString()).ToList();
            Assert.AreEqual(0, errors.Count, $"[{label}] The source must parse and compile. Errors:\n{string.Join("\n", errors)}\n{text}");
            Assert.IsNotNull(compilation.GetTypeByMetadataName("Old.Keep"), $"[{label}] Keep must stay in Old:\n{text}");
            Assert.AreEqual(alt, compilation.GetTypeByMetadataName("Old.AltKeep") is not null, $"[{label}] AltKeep must stay under its own condition:\n{text}");
            Assert.AreEqual(!alt, compilation.GetTypeByMetadataName("New.Selected") is not null, $"[{label}] Selected must move to New, under its own condition:\n{text}");
            Assert.IsNull(compilation.GetTypeByMetadataName("Old.Selected"), $"[{label}] Selected must leave Old:\n{text}");
            if (tailSibling)
                Assert.IsNotNull(compilation.GetTypeByMetadataName("Old.Tail"), $"[{label}] The active sibling after the conditional must stay in Old:\n{text}");
        }
    }

    /// <summary>Reparses every SampleLib document from its text with the given extra defines and compiles it.</summary>
    private static async Task<Microsoft.CodeAnalysis.Compilation> CompileSampleLibUnderDefinesAsync(
        Microsoft.CodeAnalysis.Solution solution, string[] defines)
    {
        var project = solution.Projects.Single(p => p.Name == "SampleLib");
        var options = ((Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)project.ParseOptions!)
            .WithPreprocessorSymbols(project.ParseOptions!.PreprocessorSymbolNames.Concat(defines));
        var trees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
        foreach (var document in project.Documents)
        {
            var text = await document.GetTextAsync(CancellationToken.None);
            trees.Add(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text, options, document.FilePath ?? document.Name));
        }
        var compilation = await project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(trees);
    }

    /// <summary>
    /// Regression (relocation-same-file-preserve-declaration-imports): the sibling-preserving
    /// branch moved only the selected type into a new namespace block; the namespace-scoped
    /// imports (aliases included) stayed behind, so the moved type lost its bindings. The retained
    /// sibling keeps its own binding.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    using Alias = System.Text.StringBuilder;\n    using System.Globalization;\n\n    public class Keep { public Alias Builder = new Alias(); }\n\n    public class Selected\n    {\n        public Alias Builder = new Alias();\n        public CultureInfo Culture = CultureInfo.InvariantCulture;\n    }\n}\n",
        "Old", DisplayName = "block namespace")]
    [DataRow("namespace Old;\n\nusing Alias = System.Text.StringBuilder;\nusing System.Globalization;\n\npublic class Keep { public Alias Builder = new Alias(); }\n\npublic class Selected\n{\n    public Alias Builder = new Alias();\n    public CultureInfo Culture = CultureInfo.InvariantCulture;\n}\n",
        "Old", DisplayName = "file-scoped namespace")]
    [DataRow("namespace Outer\n{\n    using Alias = System.Text.StringBuilder;\n\n    namespace Inner\n    {\n        using System.Globalization;\n\n        public class Keep { public Alias Builder = new Alias(); }\n\n        public class Selected\n        {\n            public Alias Builder = new Alias();\n            public CultureInfo Culture = CultureInfo.InvariantCulture;\n        }\n    }\n}\n",
        "Outer.Inner", DisplayName = "imports on two nested scopes")]
    public async Task Preview_SameFile_Moved_Type_Keeps_Its_Namespace_Scoped_Imports_And_Siblings_Keep_Theirs(
        string source, string fromNamespace)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, sourcePath, _, preview) = await PreviewRelocationAsync(
            workspace, source, relocate: false, fromNamespace: fromNamespace);

        Assert.IsNotNull(sourceDocument);
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        var errors = await CompileErrorsInAsync(modified, sourcePath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("New.Selected"), $"Selected must arrive in New:\n{text}");
        Assert.IsNotNull(compilation.GetTypeByMetadataName(fromNamespace + ".Keep"), $"Keep must stay:\n{text}");
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(text, "Alias =").Count,
            $"The retained block keeps its alias and the moved declaration gets its own:\n{text}");

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "same_file_imports", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual(text, await File.ReadAllTextAsync(sourcePath), "the applied file must equal the previewed text");
        var reloadedErrors = await CompileErrorsInAsync(reloaded, sourcePath);
        Assert.AreEqual(0, reloadedErrors.Count, $"The applied solution must compile. Errors:\n{string.Join("\n", reloadedErrors)}");
    }

    /// <summary>
    /// Same mechanism as the sibling branch: renaming the declaration in place changes what a
    /// namespace-scoped import written relative to the old namespace (<c>using Sub;</c> meaning
    /// <c>Old.Sub</c>) binds to.
    /// </summary>
    [TestMethod]
    public async Task Preview_InPlace_Rename_Keeps_Relative_Namespace_Scoped_Imports_Bound()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        const string source = "namespace Old\n{\n    using Sub;\n\n    public class Selected { public Marker Value = new Marker(); }\n}\n";
        const string other = "namespace Old.Sub\n{\n    public class Marker { }\n}\n";

        var (modified, sourceDocument, sourcePath, _, _) = await PreviewRelocationAsync(
            workspace, source, otherFile: ("SampleLib", other), relocate: false);

        Assert.IsNotNull(sourceDocument);
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        var errors = await CompileErrorsInAsync(modified, sourcePath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("New.Selected"), text);
    }

    /// <summary>
    /// The same-file paths built the destination name with an unescaped parse, so a keyword
    /// segment produced a missing-identifier namespace; the new-file path already escapes it.
    /// </summary>
    [TestMethod]
    [DataRow("namespace Old\n{\n    public class Selected { }\n}\n", DisplayName = "sole member renamed in place")]
    [DataRow("namespace Old\n{\n    public class Selected { }\n\n    public class Keep { }\n}\n", DisplayName = "extracted next to a sibling")]
    public async Task Preview_SameFile_Escapes_Keyword_Segments_In_The_Destination_Namespace(string source)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, sourceDocument, sourcePath, _, _) = await PreviewRelocationAsync(
            workspace, source, relocate: false, toNamespace: "Target.class");

        Assert.IsNotNull(sourceDocument);
        var text = (await sourceDocument.GetTextAsync(CancellationToken.None)).ToString();
        StringAssert.Contains(text, "@class", text);
        var errors = await CompileErrorsInAsync(modified, sourcePath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
        var compilation = await sourceDocument.Project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        Assert.IsNotNull(compilation.GetTypeByMetadataName("Target.class.Selected"), text);
    }

    [TestMethod]
    public async Task ArgumentRefusals_NamespaceMalformedDestinationIsNamedAndRedacted()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CreateService().PreviewChangeTypeNamespaceAsync(workspace.WorkspaceId,
                "Dog", "SampleLib", "Other", "PRIVATE-EXTRACTION-SENTINEL\0.cs", CancellationToken.None));
        Assert.AreEqual("newFilePath", error.ParamName);
        Assert.IsInstanceOfType<ArgumentException>(error.InnerException);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public async Task ArgumentRefusals_NamespaceDefaultDestinationRemainsValid(string? destination)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var preview = await CreateService().PreviewChangeTypeNamespaceAsync(workspace.WorkspaceId,
            "Dog", "SampleLib", "Other", destination, CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
    }

    [TestMethod]
    public async Task ArgumentRefusals_NamespaceWindowsLongDestinationIsNamedAndRedacted()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows path normalization enforces the maximum path length before the project-boundary check.");

        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CreateService().PreviewChangeTypeNamespaceAsync(workspace.WorkspaceId,
                "Dog", "SampleLib", "Other", new string('x', 32768), CancellationToken.None));
        Assert.AreEqual("newFilePath", error.ParamName);
        Assert.IsInstanceOfType<PathTooLongException>(error.InnerException);
    }

    private static async Task<(string Text, string ConsumerPath)> ReadConsumerAsync(
        IsolatedWorkspaceScope workspace, Microsoft.CodeAnalysis.Solution modified)
    {
        var consumerPath = Path.Combine(workspace.GetPath("SampleLib"), RelocationFixtureDirectory, "Consumer.cs");
        var document = modified.Projects.SelectMany(p => p.Documents)
            .Single(d => string.Equals(d.FilePath, consumerPath, StringComparison.OrdinalIgnoreCase));
        return ((await document.GetTextAsync(CancellationToken.None)).ToString(), consumerPath);
    }

    private static int CountImportLines(string text, string namespaceSpelling) =>
        text.Split('\n').Count(line => line.Trim() == "using " + namespaceSpelling + ";");

    private const string ConsumerOfSelected =
        "\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n}\n";

    /// <summary>
    /// Regression (relocation-using-semantic-identity): the consumer pass compared the syntax
    /// <c>Name.ToString()</c> of each import with the namespace string, so an escaped or
    /// interior-trivia spelling of the emptied old namespace was never found and stayed behind
    /// as a dangling import.
    /// </summary>
    [TestMethod]
    [DataRow("using @Old.Sub;\n", DisplayName = "escaped first segment")]
    [DataRow("using Old . Sub ;\n", DisplayName = "dotted name with interior trivia")]
    [DataRow("using global::Old.Sub;\n", DisplayName = "global-qualified")]
    public async Task Preview_Removes_Obsolete_Import_Written_In_An_Equivalent_Spelling(string oldImport)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Old.Sub\n{\n    public class Selected { }\n}\n", oldImport + ConsumerOfSelected,
            relocate: false, fromNamespace: "Old.Sub");

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        Assert.AreEqual(1, CountImportLines(text, "New"), text);
        Assert.IsFalse(text.Contains("Sub", StringComparison.Ordinal), $"The obsolete import must be removed:\n{text}");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): an existing destination import in another
    /// spelling was not recognised, so the preview added a second import of the same namespace.
    /// A duplicate import is only warning CS0105, so the import count is the red check.
    /// </summary>
    [TestMethod]
    [DataRow("New", "using @New;\n", "@New", DisplayName = "escaped destination import")]
    [DataRow("New", "using global::New;\n", "global::New", DisplayName = "global-qualified destination import")]
    [DataRow("Target.Sub", "using Target . Sub ;\n", "Target . Sub ", DisplayName = "destination import with interior trivia")]
    public async Task Preview_Does_Not_Duplicate_A_Destination_Import_Written_In_An_Equivalent_Spelling(
        string destination, string existingImport, string existingSpelling)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Old\n{\n    public class Selected { }\n}\n", "using Old;\n" + existingImport + ConsumerOfSelected,
            otherFile: ("SampleLib", "namespace " + destination + "\n{\n    public class Other { }\n}\n"),
            relocate: false, toNamespace: destination);

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        var imports = CountImportLines(text, existingSpelling) + CountImportLines(text, destination);
        Assert.AreEqual(1, imports, $"The destination must be imported exactly once:\n{text}");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): an alias import of the destination
    /// (<c>using N = New;</c>) was counted as the destination import, so no plain import was added.
    /// </summary>
    [TestMethod]
    public async Task Preview_Adds_A_Plain_Import_When_The_Destination_Is_Only_Aliased()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Old\n{\n    public class Selected { }\n}\n", "using Old;\nusing N = New;\n" + ConsumerOfSelected,
            otherFile: ("SampleLib", "namespace New\n{\n    public class Other { }\n}\n"), relocate: false);

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        Assert.AreEqual(1, CountImportLines(text, "New"), text);
        StringAssert.Contains(text, "using N = New;");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Pins the outcome for an alias of the emptied namespace: the plain import is the obsolete
    /// one and is removed; the alias is not an import of the old namespace for this decision and is
    /// left untouched (a dangling alias is a known gap tracked by its own backlog row).
    /// </summary>
    [TestMethod]
    public async Task Preview_Removes_The_Plain_Old_Import_And_Leaves_An_Alias_Of_The_Old_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, _, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Old\n{\n    public class Selected { }\n}\n", "using A = Old;\nusing Old;\n" + ConsumerOfSelected,
            relocate: false);

        var (text, _) = await ReadConsumerAsync(workspace, modified);
        Assert.AreEqual(0, CountImportLines(text, "Old"), $"The plain obsolete import must be removed:\n{text}");
        StringAssert.Contains(text, "using A = Old;");
        Assert.AreEqual(1, CountImportLines(text, "New"), text);
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): the synthesized import parsed the
    /// destination without escaping, so a keyword segment produced a missing identifier.
    /// </summary>
    [TestMethod]
    public async Task Preview_Escapes_Keyword_Segments_In_The_Synthesized_Consumer_Import()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Old\n{\n    public class Selected { }\n}\n", "using Old;\n" + ConsumerOfSelected,
            relocate: false, toNamespace: "Target.class");

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        StringAssert.Contains(text, "using Target.@class;");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): the source namespace was matched against the
    /// symbol display string, which escapes keywords, so an unescaped caller spelling never matched.
    /// </summary>
    [TestMethod]
    [DataRow("Source.class")]
    [DataRow("Source.@class")]
    public async Task Preview_Finds_The_Type_Whatever_Spelling_The_Source_Namespace_Uses(string fromNamespace)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Source.@class\n{\n    public class Selected { }\n}\n", "using Source.@class;\n" + ConsumerOfSelected,
            relocate: false, fromNamespace: fromNamespace);

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        Assert.AreEqual(1, CountImportLines(text, "New"), text);
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): the same-namespace check compared raw text,
    /// so <c>Old</c> to <c>@Old</c> passed it. It is caller input, so it is an argument refusal.
    /// </summary>
    [TestMethod]
    [DataRow("Old", "Old")]
    [DataRow("Old", "@Old")]
    [DataRow("@Old", "Old")]
    public async Task Preview_Refuses_A_Destination_That_Is_The_Same_Namespace_In_Any_Spelling(string from, string to)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => PreviewRelocationAsync(workspace,
            "namespace Old\n{\n    public class Selected { }\n}\n", relocate: false, fromNamespace: from, toNamespace: to));
        Assert.AreEqual("toNamespace", error.ParamName);
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): the consumer's own namespace was compared as
    /// raw text, so a consumer declared as <c>namespace @Target.Sub;</c> was not seen as a descendant
    /// of the destination <c>Target</c> and received a redundant import.
    /// </summary>
    [TestMethod]
    [DataRow("namespace @Target.Sub;\n")]
    [DataRow("namespace Target . Sub;\n")]
    public async Task Preview_Leaves_A_Consumer_Alone_When_Its_Declared_Namespace_Is_Under_The_Destination(string declaration)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, _) = await PreviewRelocationAsync(workspace,
            "namespace Old\n{\n    public class Selected { }\n}\n",
            "using Old;\n\n" + declaration + "\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n}\n",
            otherFile: ("SampleLib", "namespace Target\n{\n    public class Other { }\n}\n"),
            relocate: false, toNamespace: "Target");

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        Assert.AreEqual(0, CountImportLines(text, "Target"), $"Ambient resolution already covers the destination:\n{text}");
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

    /// <summary>
    /// Regression (relocation-using-semantic-identity): sibling counting split the caller's
    /// spelling into segments and compared them to symbol names, so an escaped segment found no
    /// namespace, no sibling was counted, and an import that is still needed was removed.
    /// </summary>
    [TestMethod]
    public async Task Preview_Counts_A_Sibling_Whatever_Spelling_The_Source_Namespace_Uses()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var (modified, _, sourcePath, _, preview) = await PreviewRelocationAsync(workspace,
            "namespace Source.@class\n{\n    public class Selected { }\n}\n",
            "using Source.@class;\n\nnamespace Consumers;\n\npublic sealed class Consumer\n{\n    public Selected? Value { get; set; }\n\n    public Keep? Other { get; set; }\n}\n",
            otherFile: ("SampleLib", "namespace Source.@class\n{\n    public class Keep { }\n}\n"),
            relocate: false, fromNamespace: "Source.@class");

        var (text, consumerPath) = await ReadConsumerAsync(workspace, modified);
        StringAssert.Contains(text, "using Source.@class;");
        StringAssert.Contains(text, "using New;");
        Assert.IsNotNull(preview.Warnings);
        var errors = await CompileErrorsInAsync(modified, sourcePath, consumerPath);
        Assert.AreEqual(0, errors.Count, $"The previewed solution must compile. Errors:\n{string.Join("\n", errors)}\n{text}");
    }

}
