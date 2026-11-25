using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ClassSplitReceivingNamespaceIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("Worker.Move.cs")]
    [DataRow("./Worker.cs")]
    [DataRow("alias/Worker.cs")]
    public async Task Split_RefusesOccupiedDestinationBeforeStoringPreview(string newFileName)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var directory = workspace.GetPath("SampleLib");
        var sourcePath = Path.Combine(directory, "Worker.cs");
        var existingPath = Path.Combine(directory, "Worker.Move.cs");
        const string source = "public class Worker { public int Move() => 7; }";
        const string existing = "// unrelated existing file";
        await File.WriteAllTextAsync(sourcePath, source);
        await File.WriteAllTextAsync(existingPath, existing);
        await workspace.LoadAsync();
        if (newFileName.StartsWith("alias/", StringComparison.Ordinal))
        {
            var alias = Path.Combine(directory, "alias");
            if (OperatingSystem.IsWindows())
                WindowsDirectoryJunction.Create(alias, directory);
            else
                Directory.CreateSymbolicLink(alias, directory);
        }

        var store = new CompositePreviewStore();
        var split = new ClassSplitOrchestrator(WorkspaceManager, store);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            OrchestrationTools.PreviewSplitClass(WorkspaceExecutionGate, split, workspace.WorkspaceId,
                sourcePath, "Worker", ["Move"], newFileName, CancellationToken.None));

        Assert.AreEqual(source, await File.ReadAllTextAsync(sourcePath));
        Assert.AreEqual(existing, await File.ReadAllTextAsync(existingPath));
    }

    [TestMethod]
    public async Task Split_DestinationCreatedAfterDriftCheckPreservesBothFiles()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = workspace.GetPath("SampleLib", "Worker.cs");
        var destination = workspace.GetPath("SampleLib", "Worker.Move.cs");
        const string source = "public class Worker { public int Move() => 7; }";
        const string concurrent = "// unrelated file created after drift validation";
        await File.WriteAllTextAsync(sourcePath, source);
        await workspace.LoadAsync();
        var store = new CompositePreviewStore();
        var split = new ClassSplitOrchestrator(WorkspaceManager, store);
        var preview = await split.PreviewSplitClassAsync(workspace.WorkspaceId, sourcePath, "Worker",
            ["Move"], Path.GetFileName(destination), CancellationToken.None);
        Assert.IsFalse(File.Exists(destination));
        var admission = new CompositeRedemptionAdmission(workspace.WorkspaceId,
            (_, _, ct) => File.WriteAllTextAsync(destination, concurrent, ct));
        var result = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken, admission, CancellationToken.None);

        Assert.IsFalse(result.Success, "A concurrent creator owns the destination.");
        Assert.IsEmpty(result.AppliedFiles);
        Assert.AreEqual(source, await File.ReadAllTextAsync(sourcePath));
        Assert.AreEqual(concurrent, await File.ReadAllTextAsync(destination));
    }

    [TestMethod]
    [DataRow("7.3", false, false, null, null)]
    [DataRow("9.0", false, false, null, null)]
    [DataRow("10.0", true, false, null, null)]
    [DataRow("14.0", true, false, null, null)]
    [DataRow("9.0", false, true, null, null)]
    [DataRow("9.0", false, false, "Outer", null)]
    [DataRow("14.0", true, false, "Outer", null)]
    [DataRow("14.0", true, false, "@class", null)]
    [DataRow("9.0", false, false, "Outer.Chosen", "Outer.\n#if FEATURE\nChosen\n#else\nAlternate\n#endif\n")]
    public async Task Split_Uses_Receiving_Syntax_Through_Preview_Apply_And_Reload(
        string version, bool fileScoped, bool globalNamespace, string? outerNamespace, string? outerNamespaceSource)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("PropertyGroup", new XElement("LangVersion", version),
            new XElement("Nullable", "disable"), new XElement("ImplicitUsings", "disable")));
        if (outerNamespaceSource is not null)
            xml.Root!.Add(new XElement("PropertyGroup", new XElement("DefineConstants", "FEATURE")));
        xml.Save(projectPath);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var sourcePath = workspace.GetPath("Contracts", "Worker.cs");
        var newPath = workspace.GetPath("Contracts", "Worker.Move.cs");
        var declaration = "/// <summary>Worker documentation</summary>\r\n[System.Serializable]\r\npublic class Worker\r\n{\r\n    public int Stay() => 3;\r\n    public int Move() => 7; // member-tail\r\n} // type-tail\r\n";
        var namespaceName = outerNamespace is null ? "Contracts" : $"{outerNamespace}.Contracts";
        var metadataName = globalNamespace ? "Worker" : namespaceName.Replace("@", "", StringComparison.Ordinal) + ".Worker";
        var source = globalNamespace ? declaration : "namespace Contracts\r\n{\r\n" + declaration + "}\r\n";
        if (outerNamespace is not null)
            source = $"namespace {outerNamespaceSource ?? outerNamespace}\r\n{{\r\n{source}}}\r\n";
        source = "// license stays here\r\nusing System;\r\n" + source;
        await File.WriteAllTextAsync(sourcePath, source);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var unrelatedPath = workspace.GetPath("Contracts", "Unchanged.cs");
        await File.WriteAllTextAsync(unrelatedPath, "namespace Contracts { public class Unchanged { } }\r\n");
        var unrelatedBytes = await File.ReadAllBytesAsync(unrelatedPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = before.Projects.Single(p => p.Name == "Contracts");
        Assert.IsTrue(LanguageVersionFacts.TryParse(version, out var expected));
        Assert.AreEqual(expected, ((CSharpParseOptions)project.ParseOptions!).LanguageVersion);
        await AssertCompilesAsync(before);
        await AssertRuntimeAsync(project, metadataName);
        var originalVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var store = new CompositePreviewStore();
        var split = new ClassSplitOrchestrator(WorkspaceManager, store);
        var previewJson = await OrchestrationTools.PreviewSplitClass(WorkspaceExecutionGate, split,
            workspace.WorkspaceId, sourcePath, "Worker", ["Move"], "Worker.Move.cs", CancellationToken.None);
        var preview = JsonSerializer.Deserialize<RefactoringPreviewDto>(previewJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken), previewJson);
        Assert.AreEqual(2, preview.Changes.Count);
        StringAssert.Contains(preview.Changes.Single(change => change.FilePath == sourcePath).UnifiedDiff, "partial class Worker");
        var generatedDiff = preview.Changes.Single(change => change.FilePath == newPath).UnifiedDiff;
        StringAssert.Contains(generatedDiff, "Move()");
        Assert.AreEqual(originalVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        var entry = store.Retrieve(preview.PreviewToken, null)!.Value;
        Assert.AreEqual(workspace.WorkspaceId, entry.WorkspaceId);
        Assert.AreEqual(originalVersion, entry.WorkspaceVersion);
        Assert.AreEqual(workspace.WorkspaceId, store.PeekWorkspaceId(preview.PreviewToken));
        var mutations = entry.Mutations;
        var generated = mutations.Single(m => m.FilePath == newPath).UpdatedContent!;
        var tree = CSharpSyntaxTree.ParseText(generated, (CSharpParseOptions)project.ParseOptions!, newPath);
        Assert.AreEqual(0, tree.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error),
            string.Join(Environment.NewLine, tree.GetDiagnostics()));
        var pending = before.WithDocumentText(project.Documents.Single(d => d.FilePath == sourcePath).Id,
            SourceText.From(mutations.Single(m => m.FilePath == sourcePath).UpdatedContent!));
        pending = pending.AddDocument(DocumentId.CreateNewId(project.Id), "Worker.Move.cs",
            SourceText.From(generated), filePath: newPath);
        await AssertCompilesAsync(pending);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();
        if (globalNamespace)
            Assert.IsInstanceOfType<ClassDeclarationSyntax>(root.Members.Single());
        else
        {
            var generatedNamespace = root.Members.Single() as BaseNamespaceDeclarationSyntax;
            Assert.IsNotNull(generatedNamespace);
            Assert.AreEqual(fileScoped, generatedNamespace is FileScopedNamespaceDeclarationSyntax);
            Assert.AreEqual(namespaceName, generatedNamespace.Name.ToString());
            StringAssert.Contains(generatedDiff, $"+namespace {namespaceName}");
        }
        Assert.AreEqual(0, generated.Split("license stays here").Length - 1);
        Assert.AreEqual(0, generated.Split("Worker documentation").Length - 1);
        Assert.AreEqual(0, generated.Split("[System.Serializable]").Length - 1);
        StringAssert.Contains(generated, "// member-tail");
        if (!globalNamespace && !fileScoped)
            StringAssert.Contains(generated, "// type-tail\r\n}");
        await AssertRuntimeAsync(pending.Projects.Single(p => p.Name == "Contracts"), metadataName);
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        Assert.IsFalse(File.Exists(newPath));
        var applyService = new CompositeApplyOrchestrator(WorkspaceManager, store);
        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() =>
            OrchestrationTools.ApplyComposite(WorkspaceExecutionGate, applyService, store,
                "unknown-preview-token", CancellationToken.None));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        Assert.IsFalse(File.Exists(newPath));
        var applyJson = await OrchestrationTools.ApplyComposite(WorkspaceExecutionGate, applyService, store,
            preview.PreviewToken, CancellationToken.None);
        var applied = JsonSerializer.Deserialize<ApplyResultDto>(applyJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.IsNull(store.PeekWorkspaceId(preview.PreviewToken));
        Assert.IsTrue(WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId) > originalVersion);
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual(expected, ((CSharpParseOptions)reloaded.Projects.Single(p => p.Name == "Contracts").ParseOptions!).LanguageVersion);
        await AssertCompilesAsync(reloaded);
        await AssertRuntimeAsync(reloaded.Projects.Single(p => p.Name == "Contracts"), metadataName);
        Assert.AreEqual(generated, await File.ReadAllTextAsync(newPath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        var original = await File.ReadAllTextAsync(sourcePath);
        StringAssert.Contains(original, "license stays here");
        StringAssert.Contains(original, "Worker documentation");
        StringAssert.Contains(original, "[System.Serializable]");
        StringAssert.Contains(original, "Stay()");
        Assert.IsFalse(original.Contains("Move()", StringComparison.Ordinal));
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("class", false, false)]
    [DataRow("struct", false, false)]
    [DataRow("interface", false, false)]
    [DataRow("record", false, false)]
    [DataRow("record struct", false, false)]
    [DataRow("class", true, false)]
    [DataRow("record", true, false)]
    [DataRow("class", false, true)]
    [DataRow("class-block", false, false)]
    public async Task Split_PreservesCompleteContainingIdentity(string kind, bool generic, bool global)
    {
        var blockNamespace = kind == "class-block";
        if (blockNamespace) kind = "class";
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("PropertyGroup", new XElement("LangVersion", global || blockNamespace ? "9.0" : "14.0"),
            new XElement("ImplicitUsings", "disable"), new XElement("Nullable", "disable")));
        xml.Save(projectPath);
        var sourcePath = workspace.GetPath("Contracts", "Worker.cs");
        var newPath = workspace.GetPath("Contracts", "Worker.Move.cs");
        var unrelatedPath = workspace.GetPath("Contracts", "Unchanged.txt");
        await File.WriteAllTextAsync(unrelatedPath, "unrelated\r\nbytes\n");
        var parameters = generic ? "<T>" : "";
        var constraints = generic ? " where T : class, new()" : "";
        var primary = kind == "record" && !generic ? "(int Number)" : "";
        var source = "// original-only header\n" + (global ? "" : blockNamespace ? "namespace Contracts\n{\n" : "namespace Contracts;\n") +
            "[System.Obsolete(\"fixture\")]\npublic " + kind + " @class" + parameters + primary + constraints + "\n{\n" +
            "    private static int Value => 7;\n    private class Hidden { public static int Read() => Value; }\n" +
            "    public partial class Middle\n    {\n        // selected documentation stays\n" +
            "        [System.Serializable]\n        public class Worker\n        {\n" +
            "            public int Stay() => Move();\n            public int Move() => Hidden.Read() + Value - 7; // moved-tail\n" +
            "        } // selected-tail\n    } // middle-tail\n    public class Sibling { }\n} // outer-tail\n";
        if (blockNamespace) source += "}\n";
        await File.WriteAllTextAsync(sourcePath, source);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var unrelatedBytes = await File.ReadAllBytesAsync(unrelatedPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = before.Projects.Single(p => p.Name == "Contracts");
        await AssertCompilesAsync(before);
        var metadataName = (global ? "" : "Contracts.") + "class" + (generic ? "`1" : "") + "+Middle+Worker";
        await AssertNestedRuntimeAsync(project, metadataName, generic, false);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var store = new CompositePreviewStore();
        var json = await OrchestrationTools.PreviewSplitClass(WorkspaceExecutionGate, new ClassSplitOrchestrator(WorkspaceManager, store),
            workspace.WorkspaceId, sourcePath, "Worker", ["Move"], "Worker.Move.cs", CancellationToken.None);
        TestContext.WriteLine(json);
        var preview = JsonSerializer.Deserialize<RefactoringPreviewDto>(json, JsonOptions)!;
        var original = ReconstructDiff(source, preview.Changes.Single(c => c.FilePath == sourcePath).UnifiedDiff);
        var generated = ReconstructDiff("", preview.Changes.Single(c => c.FilePath == newPath).UnifiedDiff);
        var expectedOriginal = source.Replace("public " + kind + " @class", "public partial " + kind + " @class", StringComparison.Ordinal)
            .Replace("public class Worker", "public partial class Worker", StringComparison.Ordinal)
            .Replace("public int Move() => Hidden.Read() + Value - 7;", "", StringComparison.Ordinal);
        Assert.AreEqual(expectedOriginal, original, "Only partial modifiers and the selected member may change the original.");
        var entry = store.Retrieve(preview.PreviewToken, null)!.Value;
        Assert.AreEqual(original, entry.Mutations.Single(m => m.FilePath == sourcePath).UpdatedContent);
        Assert.AreEqual(generated, entry.Mutations.Single(m => m.FilePath == newPath).UpdatedContent);
        var pending = before.WithDocumentText(project.Documents.Single(d => d.FilePath == sourcePath).Id, SourceText.From(original))
            .AddDocument(DocumentId.CreateNewId(project.Id), "Worker.Move.cs", SourceText.From(generated), filePath: newPath);
        await AssertCompilesAsync(pending);
        await AssertNestedRuntimeAsync(pending.Projects.Single(p => p.Id == project.Id), metadataName, generic, true);
        var retained = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "Stay");
        StringAssert.Contains(original, retained.ToFullString());
        StringAssert.Contains(original, "public class Sibling { }");
        StringAssert.Contains(original, "[System.Obsolete(\"fixture\")]");
        StringAssert.Contains(original, "// selected documentation stays");
        Assert.IsFalse(generated.Contains("original-only header", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("System.Obsolete", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("System.Serializable", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("selected documentation", StringComparison.Ordinal));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
        Assert.IsFalse(File.Exists(newPath));
        var result = await OrchestrationTools.ApplyComposite(WorkspaceExecutionGate,
            new CompositeApplyOrchestrator(WorkspaceManager, store), store, preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(JsonSerializer.Deserialize<ApplyResultDto>(result, JsonOptions)!.Success, result);
        Assert.IsNull(store.PeekWorkspaceId(preview.PreviewToken));
        Assert.AreEqual(original, await File.ReadAllTextAsync(sourcePath));
        Assert.AreEqual(generated, await File.ReadAllTextAsync(newPath));
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(reloaded);
        await AssertNestedRuntimeAsync(reloaded.Projects.Single(p => p.Name == "Contracts"), metadataName, generic, true);
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task Split_RefusesFileLocalIdentityWithoutEffects(int position)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("Contracts", "Worker.cs");
        var newPath = workspace.GetPath("Contracts", "Worker.Move.cs");
        var projectPath = workspace.GetPath("Contracts", "Contracts.csproj");
        var unrelatedPath = workspace.GetPath("Contracts", "Unchanged.txt");
        const string worker = "public class Worker { public int Stay() => Move(); public int Move() => 7; }";
        var source = position switch
        {
            0 => "file class Worker { public int Stay() => Move(); public int Move() => 7; }\n",
            1 => "file class Outer { " + worker + " }\n",
            _ => "file class Outer { public class Middle { " + worker + " } }\n"
        };
        await File.WriteAllTextAsync(sourcePath, source);
        await File.WriteAllTextAsync(unrelatedPath, "unrelated\r\nbytes\n");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var unrelatedBytes = await File.ReadAllBytesAsync(unrelatedPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var project = before.Projects.Single(p => p.Name == "Contracts");
        var compilation = (await project.GetCompilationAsync())!;
        var workerSyntax = compilation.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes())
            .OfType<ClassDeclarationSyntax>().Single(t => t.Identifier.ValueText == "Worker");
        var symbol = (INamedTypeSymbol)compilation.GetSemanticModel(workerSyntax.SyntaxTree).GetDeclaredSymbol(workerSyntax)!;
        var metadataName = symbol.MetadataName;
        for (var parent = symbol.ContainingType; parent is not null; parent = parent.ContainingType)
            metadataName = parent.MetadataName + "+" + metadataName;
        AssertEmittedMethods(compilation, metadataName, false);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var store = new CompositePreviewStore(maxEntries: 1);
        var sentinel = store.Store(workspace.WorkspaceId, version, "sentinel", []);
        var json = await OrchestrationTools.PreviewSplitClass(WorkspaceExecutionGate, new ClassSplitOrchestrator(WorkspaceManager, store),
            workspace.WorkspaceId, sourcePath, "Worker", ["Move"], "Worker.Move.cs", CancellationToken.None);
        TestContext.WriteLine(json);
        var preview = JsonSerializer.Deserialize<RefactoringPreviewDto>(json, JsonOptions)!;
        Assert.AreEqual("", preview.PreviewToken);
        Assert.IsEmpty(preview.Changes);
        StringAssert.Contains(preview.GuidanceMessage!, "physical file");
        Assert.AreEqual(workspace.WorkspaceId, store.PeekWorkspaceId(sentinel));
        Assert.IsNotNull(store.Retrieve(sentinel, null));
        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() => OrchestrationTools.ApplyComposite(WorkspaceExecutionGate,
            new CompositeApplyOrchestrator(WorkspaceManager, store), store, "", CancellationToken.None));
        Assert.AreEqual(workspace.WorkspaceId, store.PeekWorkspaceId(sentinel));
        Assert.IsNotNull(store.Retrieve(sentinel, null));
        Assert.IsNull(store.PeekWorkspaceId(""));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.IsFalse(File.Exists(newPath));
        CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
        CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static string ReconstructDiff(string source, string diff)
    {
        static string[] Lines(string text) => Regex.Matches(text, @"[^\r\n]*(?:\r\n|\r|\n|$)")
            .Select(m => m.Value).Where(line => line.Length != 0).ToArray();
        var old = Lines(source);
        var output = new List<string>();
        var cursor = 0;
        var inHunk = false;
        foreach (var line in Lines(diff))
        {
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                var match = Regex.Match(line, @"^@@ -(\d+),\d+ \+\d+,\d+ @@");
                Assert.IsTrue(match.Success, line);
                var start = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 1;
                output.AddRange(old[cursor..start]);
                cursor = start;
                inHunk = true;
            }
            else if (inHunk && line[0] is '+' or '-' or ' ')
            {
                var content = line[1..];
                if (line[0] != '+') Assert.AreEqual(old[cursor++], content);
                if (line[0] != '-') output.Add(content);
            }
            else if (inHunk && line.StartsWith("\\ No newline", StringComparison.Ordinal))
                output[^1] = output[^1].TrimEnd('\r', '\n');
        }
        output.AddRange(old[cursor..]);
        return string.Concat(output);
    }

    private static async Task AssertNestedRuntimeAsync(Project project, string metadataName, bool generic, bool split)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var symbol = compilation.GetTypeByMetadataName(metadataName);
        Assert.IsNotNull(symbol);
        Assert.AreEqual(split ? 2 : 1, symbol.DeclaringSyntaxReferences.Length);
        Assert.IsNull(compilation.GetTypeByMetadataName("Contracts.Worker"));
        Assert.IsNull(compilation.GetTypeByMetadataName("Worker"));
        if (split)
        {
            var syntax = await symbol.GetMembers("Move").OfType<IMethodSymbol>().Single().DeclaringSyntaxReferences.Single().GetSyntaxAsync();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var identifier in syntax.DescendantNodes().OfType<IdentifierNameSyntax>().Where(n => n.Identifier.ValueText is "Hidden" or "Value"))
            {
                var bound = model.GetSymbolInfo(identifier).Symbol;
                Assert.IsNotNull(bound, identifier.ToString());
                Assert.IsTrue(SymbolEqualityComparer.Default.Equals(symbol.ContainingType.ContainingType, bound.ContainingType));
            }
        }
        AssertEmittedMethods(compilation, metadataName, generic);
    }

    private static void AssertEmittedMethods(Compilation compilation, string metadataName, bool generic)
    {
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        bytes.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("split-" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var type = context.LoadFromStream(bytes).GetType(metadataName, true)!;
            if (generic) type = type.MakeGenericType(typeof(object));
            var instance = Activator.CreateInstance(type);
            Assert.AreEqual(7, type.GetMethod("Stay")!.Invoke(instance, null));
            Assert.AreEqual(7, type.GetMethod("Move")!.Invoke(instance, null));
        }
        finally { context.Unload(); }
    }

    private static async Task AssertRuntimeAsync(Project project, string metadataName)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.IsNotNull(type);
        Assert.AreEqual(2, type.GetMembers().OfType<IMethodSymbol>().Count(m => m.Name is "Stay" or "Move"));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        bytes.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ClassSplit_" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var runtimeType = context.LoadFromStream(bytes).GetType(metadataName, throwOnError: true)!;
            var instance = Activator.CreateInstance(runtimeType)!;
            Assert.AreEqual(3, runtimeType.GetMethod("Stay")!.Invoke(instance, null));
            Assert.AreEqual(7, runtimeType.GetMethod("Move")!.Invoke(instance, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
