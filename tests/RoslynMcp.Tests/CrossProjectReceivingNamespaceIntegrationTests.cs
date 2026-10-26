using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectReceivingNamespaceIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    public static IEnumerable<object[]> ReceivingCases()
    {
        foreach (var route in new[] { "cross-project", "dependency-inversion", "extract-and-wire", "move" })
            foreach (var version in new[] { "7.3", "9.0", "10.0", "14.0" })
                yield return [route, version];
    }

    [TestMethod]
    [DynamicData(nameof(ReceivingCases))]
    public async Task Receiving_Syntax_Compiles_Through_Serialized_Preview_Apply_And_Reload(string route, string version)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var targetProjectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        SetReceivingPolicy(targetProjectPath, version);
        var targetProjectBytes = await File.ReadAllBytesAsync(targetProjectPath);
        var sourcePath = workspace.GetPath("SampleLib", "EchoService.cs");
        const string source = "namespace Contracts { public class EchoService { public int Echo(int value) => value + 1; } }";
        await File.WriteAllTextAsync(sourcePath, source);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var unrelatedPath = workspace.GetPath("SampleLib", "Unchanged.cs");
        const string unrelated = "// unrelated bytes\r\nnamespace Contracts; public class Unchanged { }\r\n";
        await File.WriteAllTextAsync(unrelatedPath, unrelated);
        var unrelatedBytes = await File.ReadAllBytesAsync(unrelatedPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        await AssertCompilesAsync(before);
        await AssertRuntimeAsync(before, false, false, "Contracts");
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var compositeStore = new CompositePreviewStore();
        RefactoringPreviewDto preview = route switch
        {
            "cross-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "EchoService", "IEchoService", "Contracts", CancellationToken.None),
            "dependency-inversion" => await CrossProjectRefactoringService.PreviewDependencyInversionAsync(workspace.WorkspaceId,
                sourcePath, "EchoService", "IEchoService", "Contracts", CancellationToken.None),
            "extract-and-wire" => await new ExtractAndWireOrchestrator(WorkspaceManager, compositeStore, PreviewStore,
                CrossProjectRefactoringService, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId,
                    sourcePath, "EchoService", "IEchoService", "Contracts", true, CancellationToken.None),
            "move" => await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(workspace.WorkspaceId,
                sourcePath, "EchoService", "Contracts", null, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, compositeStore.Retrieve(preview.PreviewToken)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        // This is the causal regression oracle: compile the actual published text with the receiving options.
        await AssertCompilesAsync(pending);
        var targetDocument = pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d =>
            d.Name == (route == "move" ? "EchoService.cs" : "IEchoService.cs"));
        var root = (CompilationUnitSyntax)(await targetDocument.GetSyntaxRootAsync())!;
        Assert.AreEqual(version is "7.3" or "9.0", root.Members.Single() is NamespaceDeclarationSyntax);
        if (route != "move" && version is "10.0" or "14.0")
            Assert.AreEqual("using System;\r\n\r\nnamespace Contracts;\r\npublic interface IEchoService\r\n{\r\n    int Echo(int value);\r\n}",
                (await targetDocument.GetTextAsync()).ToString());
        if (route == "move" && version is "10.0" or "14.0")
            Assert.AreEqual("namespace Contracts;\r\n" + CSharpSyntaxTree.ParseText(source).GetRoot()
                .DescendantNodes().OfType<ClassDeclarationSyntax>().Single().ToFullString(),
                (await targetDocument.GetTextAsync()).ToString());
        await AssertRuntimeAsync(pending, route == "move", route != "move", "Contracts");
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(targetProjectBytes, await File.ReadAllBytesAsync(targetProjectPath));
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.IsFalse(File.Exists(targetDocument.FilePath));

        var result = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "namespace_test", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        await AssertRuntimeAsync(applied, route == "move", route != "move", "Contracts");
        CollectionAssert.AreEqual(targetProjectBytes, await File.ReadAllBytesAsync(targetProjectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        if (route == "move") Assert.IsFalse(File.Exists(sourcePath));
        foreach (var document in applied.Projects.SelectMany(p => p.Documents).Where(d => d.Name is "EchoService.cs" or "IEchoService.cs"))
            Assert.AreEqual((await document.GetTextAsync()).ToString(), await File.ReadAllTextAsync(document.FilePath!));
        Assert.IsTrue(applied.Projects.Single(p => p.Name == "SampleLib").ProjectReferences.Any(r =>
            r.ProjectId == applied.Projects.Single(p => p.Name == "Contracts").Id));
    }

    public static IEnumerable<object[]> MoveCases()
    {
        foreach (var version in new[] { "7.3", "9.0", "10.0", "14.0" })
            foreach (var selection in new[] { "explicit", "preserve", "global", "derive" })
                yield return [version, selection];
    }

    [TestMethod]
    [DynamicData(nameof(MoveCases))]
    public async Task Move_Preserves_Member_Trivia_And_Namespace_Selection(string version, string selection)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        SetReceivingPolicy(projectPath, version);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var path = workspace.GetPath("SampleLib", "EchoService.cs");
        const string body = "/// <summary>Echo service.</summary>\r\npublic class EchoService\r\n{\r\n#region body-region\r\n    public int Echo(int value)\r\n    {\r\n        const string literal = @\"a  b\";\r\n        return value + literal.Length - 3;\r\n    }\r\n#endregion\r\n} // member-tail";
        var source = selection switch
        {
            "explicit" => "// file-banner\r\nusing System;\r\nnamespace Original\r\n{\r\n" + body + "\r\npublic class Kept { }\r\n}\r\n// file-tail\r\n",
            "preserve" => "// file-banner\r\nusing System;\r\nnamespace Original;\r\n" + body + "\r\n// file-tail\r\n",
            _ => "// file-banner\r\n" + body,
        };
        await File.WriteAllTextAsync(path, source);
        var sourceBytes = await File.ReadAllBytesAsync(path);
        await workspace.LoadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var original = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "EchoService");
        var originalNamespace = selection is "explicit" or "preserve" ? "Original" : "";
        await AssertRuntimeAsync(before, false, false, originalNamespace);
        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(workspace.WorkspaceId,
            path, "EchoService", "Contracts", selection == "explicit" ? "Chosen.Namespace" : null,
            CancellationToken.None, preserveNamespace: selection is "preserve" or "global");
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        var moved = pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "EchoService.cs");
        var text = (await moved.GetTextAsync()).ToString();
        var root = (CompilationUnitSyntax)(await moved.GetSyntaxRootAsync())!;
        var member = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var requiredBoundary = selection != "global" && version is "7.3" or "9.0" && !original.ToFullString().EndsWith('\n') ? "\r\n" : "";
        var expectedMember = original.ToFullString();
        if (selection == "derive") expectedMember = expectedMember["// file-banner\r\n".Length..];
        Assert.AreEqual(expectedMember + requiredBoundary, member.ToFullString(),
            "Preserve declaration docs/body/trivia, transfer only the true file banner before a new namespace, and add only a required brace boundary.");
        Assert.AreEqual(1, Count(text, "// file-banner"));
        Assert.AreEqual(1, Count(text, "// member-tail"));
        Assert.AreEqual(1, member.GetLeadingTrivia().Count(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)));
        if (selection is "explicit" or "preserve") Assert.AreEqual(1, Count(text, "// file-tail"));
        var expectedNamespace = selection switch { "explicit" => "Chosen.Namespace", "preserve" => "Original", "global" => "", _ => "Contracts" };
        Assert.AreEqual(expectedNamespace, member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SingleOrDefault()?.Name.ToString() ?? "");
        Assert.AreEqual(selection != "global" && version is "7.3" or "9.0", root.Members.Single() is NamespaceDeclarationSyntax);
        await AssertRuntimeAsync(pending, true, false, expectedNamespace);
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "namespace_move_trivia", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        await AssertRuntimeAsync(applied, true, false, expectedNamespace);
        Assert.AreEqual(text, await File.ReadAllTextAsync(moved.FilePath!));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        if (selection == "explicit")
        {
            var remaining = await File.ReadAllTextAsync(path);
            StringAssert.Contains(remaining, "public class Kept { }");
            Assert.IsFalse(remaining.Contains("public class EchoService", StringComparison.Ordinal));
        }
        else if (selection == "preserve") Assert.IsFalse(File.Exists(path));
    }

    private static int Count(string text, string marker) => text.Split(marker, StringSplitOptions.None).Length - 1;

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public async Task Move_Preserves_File_Generation_Context_And_Declaration_Documentation(
        bool selectedFirst, bool wrapNamespace, bool explicitNullable)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        SetReceivingPolicy(projectPath, "14.0");
        var xml = XDocument.Load(projectPath);
        xml.Descendants("Nullable").Last().Value = "enable";
        xml.Save(projectPath);
        const string selected = "/// <summary>SELECTED DOC</summary>\r\npublic class EchoService { public string Identity(string value) => value; public int Echo(int value) => value + 1; }\r\n";
        const string other = "/// <summary>OTHER DOC</summary>\r\npublic class Other { public string Identity(string value) => value; }\r\n";
        var prelude = "// <auto-generated/>\r\n" + (explicitNullable ? "#nullable enable\r\n" : "");
        var source = prelude + (selectedFirst ? selected + other : other + selected);
        var path = workspace.GetPath("SampleLib", "EchoService.cs");
        await File.WriteAllTextAsync(path, source);
        var bytes = await File.ReadAllBytesAsync(path);
        await workspace.LoadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        await AssertContextAsync(before, "SampleLib", "EchoService", "SELECTED DOC");
        await AssertContextAsync(before, "SampleLib", "Other", "OTHER DOC");
        await AssertRuntimeAsync(before, false, false, "");
        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(workspace.WorkspaceId,
            path, "EchoService", "Contracts", null, CancellationToken.None, preserveNamespace: !wrapNamespace);
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        await AssertContextAsync(pending, "SampleLib", "Other", "OTHER DOC");
        await AssertContextAsync(pending, "Contracts", wrapNamespace ? "Contracts.EchoService" : "EchoService", "SELECTED DOC");
        var target = pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "EchoService.cs");
        var targetText = (await target.GetTextAsync()).ToString();
        Assert.IsTrue(targetText.StartsWith(prelude, StringComparison.Ordinal), "The true file prelude precedes any namespace.");
        Assert.AreEqual(1, Count(targetText, "// <auto-generated/>"));
        Assert.AreEqual(0, Count(targetText, "OTHER DOC"));
        var originalBody = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "EchoService").ToString();
        Assert.AreEqual(originalBody, (await target.GetSyntaxRootAsync())!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single().ToString());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "generated_file_prelude", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        await AssertContextAsync(applied, "SampleLib", "Other", "OTHER DOC");
        await AssertContextAsync(applied, "Contracts", wrapNamespace ? "Contracts.EchoService" : "EchoService", "SELECTED DOC");
        await AssertRuntimeAsync(applied, true, false, wrapNamespace ? "Contracts" : "");
        Assert.AreEqual(targetText, await File.ReadAllTextAsync(target.FilePath!));
        var remaining = await File.ReadAllTextAsync(path);
        Assert.IsTrue(remaining.StartsWith(prelude, StringComparison.Ordinal));
        Assert.AreEqual(1, Count(remaining, "// <auto-generated/>"));
        Assert.AreEqual(0, Count(remaining, "SELECTED DOC"));
        StringAssert.Contains(remaining, other);

        async Task AssertContextAsync(Solution solution, string projectName, string metadataName, string documentation)
        {
            var project = solution.Projects.Single(p => p.Name == projectName);
            Assert.AreEqual(LanguageVersion.CSharp14, ((CSharpParseOptions)project.ParseOptions!).LanguageVersion);
            var compilation = (await project.GetCompilationAsync())!;
            Assert.AreEqual(NullableContextOptions.Enable, compilation.Options.NullableContextOptions);
            var type = compilation.GetTypeByMetadataName(metadataName)!;
            var declaration = await type.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
            var tree = declaration.SyntaxTree;
            var generatedMethod = typeof(Compilation).Assembly.GetType("Roslyn.Utilities.GeneratedCodeUtilities")!
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Single(m => m.Name == "IsGeneratedCode" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(SyntaxTree));
            Assert.AreEqual(true, generatedMethod.Invoke(null, [tree,
                new Func<SyntaxTrivia, bool>(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)), CancellationToken.None]),
                "Compiler generated-file classification must survive independently of explicit nullable directives.");
            var method = type.GetMembers("Identity").OfType<IMethodSymbol>().Single();
            var expected = explicitNullable ? NullableAnnotation.NotAnnotated : NullableAnnotation.None;
            Assert.AreEqual(expected, method.ReturnNullableAnnotation);
            Assert.AreEqual(expected, method.Parameters.Single().NullableAnnotation);
            var docs = type.GetDocumentationCommentXml()!;
            StringAssert.Contains(docs, documentation);
            Assert.IsFalse(docs.Contains(documentation == "SELECTED DOC" ? "OTHER DOC" : "SELECTED DOC", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow("7.3", false)]
    [DataRow("7.3", true)]
    [DataRow("14.0", false)]
    [DataRow("14.0", true)]
    public async Task Move_Second_Type_Preserves_Documentation_Ownership(string version, bool wrapNamespace)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        SetReceivingPolicy(workspace.GetPath("Contracts", "Contracts.csproj"), version);
        var path = workspace.GetPath("SampleLib", "EchoService.cs");
        const string source = "/// <summary>FIRST DOCUMENTATION</summary>\r\npublic class First { }\r\n\r\n/// <summary>SELECTED DOCUMENTATION</summary>\r\npublic class EchoService { public int Echo(int value) => value + 1; }\r\n";
        await File.WriteAllTextAsync(path, source);
        var sourceBytes = await File.ReadAllBytesAsync(path);
        await workspace.LoadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        await AssertRuntimeAsync(before, false, false, "");
        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(workspace.WorkspaceId,
            path, "EchoService", "Contracts", null, CancellationToken.None, preserveNamespace: !wrapNamespace);
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        var moved = pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "EchoService.cs");
        var text = (await moved.GetTextAsync()).ToString();
        Assert.AreEqual(0, Count(text, "FIRST DOCUMENTATION"), "An unrelated declaration owns its documentation.");
        Assert.AreEqual(1, Count(text, "SELECTED DOCUMENTATION"));
        var originalMember = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.ValueText == "EchoService");
        var movedMember = (await moved.GetSyntaxRootAsync())!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        Assert.AreEqual(originalMember.ToFullString(), movedMember.ToFullString());
        await AssertFirstDocumentationAsync(pending);
        await AssertRuntimeAsync(pending, true, false, wrapNamespace ? "Contracts" : "");
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(path));
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "second_type_documentation", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        await AssertFirstDocumentationAsync(applied);
        await AssertRuntimeAsync(applied, true, false, wrapNamespace ? "Contracts" : "");
        Assert.AreEqual(text, await File.ReadAllTextAsync(moved.FilePath!));

        static async Task AssertFirstDocumentationAsync(Solution solution)
        {
            var compilation = (await solution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
            var first = compilation.GetTypeByMetadataName("First")!;
            var declaration = (ClassDeclarationSyntax)await first.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
            StringAssert.Contains(declaration.ToFullString(), "/// <summary>FIRST DOCUMENTATION</summary>\r\npublic class First { }");
            var docs = first.GetDocumentationCommentXml()!;
            StringAssert.Contains(docs, "FIRST DOCUMENTATION");
            Assert.IsFalse(docs.Contains("SELECTED DOCUMENTATION", StringComparison.Ordinal));
            Assert.AreEqual(0, first.GetMembers().OfType<IMethodSymbol>().Count(m => m.Name == "Echo"));
        }
    }

    [TestMethod]
    public async Task Same_Project_Global_Interface_Remains_Unwrapped()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "EchoService.cs");
        await File.WriteAllTextAsync(path, "public class EchoService { public int Echo(int value) => value + 1; }");
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var bytes = await File.ReadAllBytesAsync(path);
        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            path, "EchoService", "IEchoService", null, CancellationToken.None);
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        var contract = pending.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.Name == "IEchoService.cs");
        var root = (CompilationUnitSyntax)(await contract.GetSyntaxRootAsync())!;
        Assert.IsInstanceOfType<InterfaceDeclarationSyntax>(root.Members.Single());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "global_namespace", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        Assert.AreEqual((await contract.GetTextAsync()).ToString(), await File.ReadAllTextAsync(contract.FilePath!));
    }

    [TestMethod]
    [DataRow("7.3")]
    [DataRow("9.0")]
    public async Task Move_Block_Wrapper_Separates_Leading_Directives_And_Closing_Brace(string version)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        SetReceivingPolicy(projectPath, version);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var path = workspace.GetPath("SampleLib", "EchoService.cs");
        const string member = "#region selected\r\n#pragma warning disable CS0618\r\npublic class EchoService { public int Echo(int value) => value + 1; } // member-tail\r\n#endregion\r\n";
        const string source = "// file-banner\r\nnamespace Original\r\n{\r\n" + member + "}\r\n";
        await File.WriteAllTextAsync(path, source);
        var sourceBytes = await File.ReadAllBytesAsync(path);
        await workspace.LoadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(workspace.WorkspaceId,
            path, "EchoService", "Contracts", null, CancellationToken.None);
        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        await AssertRuntimeAsync(pending, true, false, "Contracts");
        var moved = pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "EchoService.cs");
        var text = (await moved.GetTextAsync()).ToString();
        StringAssert.Contains(text, "{\r\n#region selected\r\n#pragma warning disable CS0618\r\n");
        StringAssert.Contains(text, "// member-tail\r\n#endregion\r\n}");
        Assert.AreEqual(1, Count(text, "// file-banner"));
        Assert.AreEqual(1, Count(text, "#region selected"));
        Assert.AreEqual(1, Count(text, "#endregion"));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "namespace_directive_boundary", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, version);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        await AssertRuntimeAsync(applied, true, false, "Contracts");
        Assert.AreEqual(text, await File.ReadAllTextAsync(moved.FilePath!));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        Assert.IsFalse(File.Exists(path));
    }

    private static void SetReceivingPolicy(string projectPath, string version)
    {
        var xml = XDocument.Load(projectPath);
        var group = new XElement("PropertyGroup", new XElement("LangVersion", version),
            new XElement("Nullable", "disable"), new XElement("ImplicitUsings", "disable"),
            new XElement("TreatWarningsAsErrors", "true"));
        xml.Root!.Add(group);
        xml.Save(projectPath);
    }

    private static async Task AssertPolicyAsync(string workspaceId, string version)
    {
        var status = WorkspaceManager.GetStatus(workspaceId);
        var errors = status.WorkspaceDiagnostics.Where(d => d.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.Message)));
        foreach (var project in WorkspaceManager.GetCurrentSolution(workspaceId).Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            Assert.AreEqual(project.Name == "Contracts" ? NullableContextOptions.Disable : NullableContextOptions.Enable,
                compilation.Options.NullableContextOptions, project.Name);
            if (project.Name == "Contracts")
            {
                Assert.IsTrue(LanguageVersionFacts.TryParse(version, out var expected));
                Assert.AreEqual(expected, ((CSharpParseOptions)project.ParseOptions!).LanguageVersion);
                Assert.IsTrue(compilation.Options.GeneralDiagnosticOption == ReportDiagnostic.Error);
            }
        }
    }

    private static async Task AssertRuntimeAsync(Solution solution, bool moved, bool hasInterface, string namespaceName)
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("ReceivingNamespace_" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var assemblies = new Dictionary<string, System.Reflection.Assembly>();
            context.Resolving += (_, name) => assemblies.GetValueOrDefault(name.Name!);
            foreach (var name in new[] { "Contracts", "SampleLib" })
            {
                var compilation = (await solution.Projects.Single(p => p.Name == name).GetCompilationAsync())!;
                using var bytes = new MemoryStream();
                var result = compilation.Emit(bytes);
                Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
                bytes.Position = 0;
                assemblies.Add(compilation.AssemblyName!, context.LoadFromStream(bytes));
            }
            var concrete = assemblies[moved ? "Contracts" : "SampleLib"].GetType((namespaceName.Length == 0 ? "" : namespaceName + ".") + "EchoService")!;
            Assert.IsNotNull(concrete);
            var instance = Activator.CreateInstance(concrete)!;
            var method = hasInterface ? assemblies["Contracts"].GetType("Contracts.IEchoService")!.GetMethod("Echo")! : concrete.GetMethod("Echo")!;
            if (hasInterface) Assert.IsTrue(method.DeclaringType!.IsAssignableFrom(concrete));
            Assert.AreEqual(8, method.Invoke(instance, [7]));
        }
        finally
        {
            context.Unload();
        }
    }
}
