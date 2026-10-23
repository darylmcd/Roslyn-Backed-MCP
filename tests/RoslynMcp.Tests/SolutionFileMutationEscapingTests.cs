using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed partial class SolutionFileMutationEscapingTests
{
    [TestMethod]
    [DataRow("Peer;Extra.csproj")]
    [DataRow("Peer%3BExtra.csproj")]
    [DataRow("Peer$(Name).csproj")]
    [DataRow("Peer@(Names).csproj")]
    [DataRow("Peer%(Identity).csproj")]
    public Task AddedProjectReference_PreservesOneLiteralNativeItem(string name) =>
        WithFixtureAsync(async fixture =>
        {
            var peerPath = Path.Combine(fixture.Root, name);
            await File.WriteAllTextAsync(peerPath, "<Project />");
            var peer = ProjectId.CreateNewId();
            var original = fixture.Solution.AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer",
                LanguageNames.CSharp, filePath: peerPath));
            var modified = original.AddProjectReference(fixture.Owner, new ProjectReference(peer));
            var rendered = await RenderAsync(fixture, original, modified);
            var items = Evaluate(fixture.ProjectPath, rendered, "ProjectReference");
            Assert.HasCount(1, items, "A reviewed reference denotes exactly one native item.");
            Assert.AreEqual(peerPath, items[0].Path);
            CollectionAssert.AreEqual(fixture.Before, await File.ReadAllBytesAsync(fixture.ProjectPath), "Rendering must not apply source bytes.");
        });

    [TestMethod]
    [DataRow("Peer;Extra.csproj", false)]
    [DataRow("Peer;Extra.csproj", true)]
    [DataRow("Peer%3BExtra.csproj", false)]
    [DataRow("Peer%3BExtra.csproj", true)]
    [DataRow("Peer$(Name).csproj", false)]
    [DataRow("Peer$(Name).csproj", true)]
    public Task ImportedProjectReference_RemoveOrUpdatePreservesLiteralTarget(string name, bool update) =>
        WithFixtureAsync(async fixture =>
        {
            var peerPath = Path.Combine(fixture.Root, name);
            await File.WriteAllTextAsync(peerPath, "<Project />");
            await fixture.SetImportedItemAsync("ProjectReference", peerPath);
            var initial = Evaluate(fixture.ProjectPath, fixture.Before, "ProjectReference");
            Assert.HasCount(1, initial, "The actual imported item must identify the intended literal target before rendering.");
            Assert.AreEqual(peerPath, initial[0].Path);
            var peer = ProjectId.CreateNewId();
            var original = fixture.Solution.AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer",
                LanguageNames.CSharp, filePath: peerPath)).AddProjectReference(fixture.Owner, new ProjectReference(peer));
            var modified = original.RemoveProjectReference(fixture.Owner, original.GetProject(fixture.Owner)!.ProjectReferences.Single());
            if (update) modified = modified.AddProjectReference(fixture.Owner, new ProjectReference(peer, ["global", "library"], true));
            var rendered = await RenderAsync(fixture, original, modified);
            var items = Evaluate(fixture.ProjectPath, rendered, "ProjectReference");
            Assert.HasCount(update ? 1 : 0, items);
            if (update)
            {
                Assert.AreEqual(peerPath, items[0].Path);
                Assert.AreEqual("global,library", items[0].Aliases, "Alias list separators retain their protocol meaning.");
                Assert.AreEqual("true", items[0].EmbedInteropTypes);
            }
            CollectionAssert.AreEqual(fixture.Before, await File.ReadAllBytesAsync(fixture.ProjectPath));
        });

    public static IEnumerable<object[]> DocumentCases =>
        from kind in new[] { "Compile", "AdditionalFiles", "EditorConfigFiles" }
        from name in new[] { "A;B.cs", "A%3BB.cs", "A$(Name).cs", "A@(Names).cs", "A%(Identity).cs" }
        from remove in new[] { false, true }
        select new object[] { kind, name, remove };

    [TestMethod]
    [DynamicData(nameof(DocumentCases))]
    public Task ExternalDocumentMembership_PreservesLiteralItemAndLink(string kind, string name, bool remove) =>
        WithFixtureAsync(async fixture =>
        {
            var external = Path.Combine(fixture.Root, "external", name);
            Directory.CreateDirectory(Path.GetDirectoryName(external)!);
            await File.WriteAllTextAsync(external, "// owned document");
            if (remove) await fixture.SetImportedItemAsync(kind, external);
            var initial = Evaluate(fixture.ProjectPath, fixture.Before, kind);
            Assert.HasCount(remove ? 1 : 0, initial);
            if (remove) Assert.AreEqual(external, initial[0].Path);
            var document = DocumentId.CreateNewId(fixture.Owner);
            var withDocument = AddDocument(fixture.Solution, kind, document, name, external);
            var original = remove ? withDocument : fixture.Solution;
            var modified = remove ? RemoveDocument(original, kind, document) : withDocument;
            var rendered = await RenderAsync(fixture, original, modified);
            var items = Evaluate(fixture.ProjectPath, rendered, kind);
            Assert.HasCount(remove ? 0 : 1, items, "Membership must address only the exact reviewed native item.");
            if (!remove)
            {
                Assert.AreEqual(external, items[0].Path);
                if (kind == "Compile") Assert.AreEqual(Path.Combine("Virtual;Folder%3B", name), items[0].Link);
            }
            CollectionAssert.AreEqual(fixture.Before, await File.ReadAllBytesAsync(fixture.ProjectPath));
            Assert.AreEqual("// owned document", await File.ReadAllTextAsync(external));
        });

    [TestMethod]
    [DataRow("Literal;Name.cs")]
    [DataRow("Literal%3BName.cs")]
    [DataRow("Literal$(Name).cs")]
    [DataRow("Literal@(Names).cs")]
    [DataRow("Literal%(Identity).cs")]
    public Task AuthoredLiteralMatcher_DecodesEscapesExactlyOnce(string name) =>
        WithFixtureAsync(fixture =>
        {
            var path = Path.Combine(fixture.Root, name);
            var include = Escape(Path.GetRelativePath(Path.GetDirectoryName(fixture.ProjectPath)!, path));
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("ItemGroup",
                new XElement("Compile", new XAttribute("Include", include)))).ToString());
            var actual = Evaluate(fixture.ProjectPath, bytes, "Compile");
            Assert.HasCount(1, actual);
            Assert.AreEqual(path, actual[0].Path, "Native item expansion establishes the exact escaped filename premise.");
            Assert.IsTrue(SolutionFileMutationPlanner.ProjectReferenceTargetsPath(include,
                Path.GetDirectoryName(fixture.ProjectPath)!, actual[0].Path));
            return Task.CompletedTask;
        });

    [TestMethod]
    [DataRow("$(Name).cs")]
    [DataRow("@(Names).cs")]
    [DataRow("%(Identity).cs")]
    [DataRow("A;B.cs")]
    [DataRow("*.cs")]
    [DataRow("A?.cs")]
    public Task AuthoredExpressionMatcher_DoesNotTreatExpressionAsLiteral(string expression) =>
        WithFixtureAsync(fixture =>
        {
            Assert.IsFalse(SolutionFileMutationPlanner.ProjectReferenceTargetsPath(expression,
                fixture.Root, Path.Combine(fixture.Root, expression)));
            return Task.CompletedTask;
        });

    [TestMethod]
    public Task AuthoredLiteralMatcher_UsesNativePathSeparators() =>
        WithFixtureAsync(fixture =>
        {
            const string include = "nested\\Input.cs";
            var bytes = Encoding.UTF8.GetBytes(new XElement("Project", new XElement("ItemGroup",
                new XElement("Compile", new XAttribute("Include", include)))).ToString());
            var actual = Evaluate(fixture.ProjectPath, bytes, "Compile");
            Assert.HasCount(1, actual);
            Assert.AreEqual(Path.Combine(Path.GetDirectoryName(fixture.ProjectPath)!, "nested", "Input.cs"), actual[0].Path);
            Assert.IsTrue(SolutionFileMutationPlanner.ProjectReferenceTargetsPath(include,
                Path.GetDirectoryName(fixture.ProjectPath)!, actual[0].Path));
            return Task.CompletedTask;
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SolutionMembership_PreservesRawPercentEscapeFilename(bool remove) =>
        WithFixtureAsync(async fixture =>
        {
            var solutionPath = Path.Combine(fixture.Root, "Owned.slnx");
            var peerPath = Path.Combine(fixture.Root, "Peer%3BExtra.csproj");
            await File.WriteAllTextAsync(peerPath, "<Project />");
            using var workspace = new AdhocWorkspace();
            var original = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: solutionPath))
                .AddProject(ProjectInfo.Create(fixture.Owner, VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: fixture.ProjectPath));
            var peer = ProjectId.CreateNewId();
            var withPeer = original.AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer", LanguageNames.CSharp, filePath: peerPath));
            if (remove) original = withPeer;
            var modified = remove ? original.RemoveProject(peer) : withPeer;
            var definition = new XElement("Solution", new XElement("Project", new XAttribute("Path", Path.GetRelativePath(fixture.Root, fixture.ProjectPath))));
            if (remove) definition.Add(new XElement("Project", new XAttribute("Path", "Peer%3BExtra.csproj")));
            var before = Encoding.UTF8.GetBytes(definition.ToString());
            await File.WriteAllBytesAsync(solutionPath, before);
            var mutations = await SolutionFileMutationPlanner.CreateAsync(original, modified,
                async (path, ct) => File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null,
                new CompilerReferenceAuthority(new Dictionary<string, CompilerReferenceAuthority.ProjectEdges>()),
                includeDocuments: false, allowVirtualPaths: false, CancellationToken.None);
            var rendered = mutations.Single(mutation => mutation.Path == solutionPath).After!;
            var projects = XDocument.Parse(Encoding.UTF8.GetString(rendered)).Descendants("Project").ToArray();
            Assert.HasCount(remove ? 1 : 2, projects);
            Assert.AreEqual(remove ? 0 : 1, projects.Count(project => (string?)project.Attribute("Path") == "Peer%3BExtra.csproj"),
                "Solution Path values are raw solution-format paths, not MSBuild escaped item specifications.");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(solutionPath));
        });

    [TestMethod]
    public Task SolutionMembership_UsesNativeBackslashSeparatorWithoutDecodingLiteralPercent() =>
        WithFixtureAsync(async fixture =>
        {
            var solutionPath = Path.Combine(fixture.Root, "NativeSeparators.slnx");
            var peerPath = Path.Combine(fixture.Root, "nested", "Peer%3BExtra.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(peerPath)!);
            await File.WriteAllTextAsync(peerPath, "<Project />");
            var before = Encoding.UTF8.GetBytes(new XElement("Solution",
                new XElement("Project", new XAttribute("Path", Path.GetRelativePath(fixture.Root, fixture.ProjectPath))),
                new XElement("Project", new XAttribute("Path", "nested\\Peer%3BExtra.csproj"))).ToString());
            await File.WriteAllBytesAsync(solutionPath, before);
            var native = await new DotnetCommandRunner().RunAsync(fixture.Root, solutionPath,
                ["sln", solutionPath, "list"], CancellationToken.None);
            Assert.IsTrue(native.Succeeded, native.StdErr);
            StringAssert.Contains(native.StdOut.Replace('\\', '/'), "nested/Peer%3BExtra.csproj",
                "The installed solution reader must identify the literal filename under native separator rules.");
            using var workspace = new AdhocWorkspace();
            var peer = ProjectId.CreateNewId();
            var original = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: solutionPath))
                .AddProject(ProjectInfo.Create(fixture.Owner, VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: fixture.ProjectPath))
                .AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer", LanguageNames.CSharp, filePath: peerPath));
            var mutations = await SolutionFileMutationPlanner.CreateAsync(original, original.RemoveProject(peer),
                async (path, ct) => File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null,
                new CompilerReferenceAuthority(new Dictionary<string, CompilerReferenceAuthority.ProjectEdges>()),
                includeDocuments: false, allowVirtualPaths: false, CancellationToken.None);
            var rendered = mutations.Single(mutation => mutation.Path == solutionPath).After!;
            var projects = XDocument.Parse(Encoding.UTF8.GetString(rendered)).Descendants("Project").ToArray();
            Assert.HasCount(1, projects, "A native backslash path must remove the exact reviewed solution member on Linux too.");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(solutionPath));
        });

    [TestMethod]
    [DataRow("optimization", false)]
    [DataRow("optimization", true)]
    [DataRow("diagnostics", false)]
    [DataRow("diagnostics", true)]
    public Task AnalyzerConfigDelta_DoesNotAuthorizeUnrepresentedCompilationOptions(string option, bool remove) =>
        WithFixtureAsync(async fixture =>
        {
            var configPath = Path.Combine(Path.GetDirectoryName(fixture.ProjectPath)!, ".editorconfig");
            await File.WriteAllTextAsync(configPath, "root = true\n[*.cs]\ndotnet_diagnostic.CS0168.severity = error\n");
            var id = DocumentId.CreateNewId(fixture.Owner);
            var withConfig = fixture.Solution.AddAnalyzerConfigDocument(id, ".editorconfig", SourceText.From(await File.ReadAllTextAsync(configPath)), filePath: configPath);
            var original = remove ? withConfig : fixture.Solution;
            var modified = remove ? withConfig.RemoveAnalyzerConfigDocument(id) : withConfig;
            var options = (CSharpCompilationOptions)modified.GetProject(fixture.Owner)!.CompilationOptions!;
            options = option == "optimization" ? options.WithOptimizationLevel(OptimizationLevel.Release)
                : options.WithGeneralDiagnosticOption(ReportDiagnostic.Error);
            modified = modified.WithProjectCompilationOptions(fixture.Owner, options);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RenderAsync(fixture, original, modified));
            CollectionAssert.AreEqual(fixture.Before, await File.ReadAllBytesAsync(fixture.ProjectPath));
        });

    [TestMethod]
    public Task AnalyzerConfigDelta_RetainsDerivedDiagnosticProviderAndNativeMembership() =>
        WithFixtureAsync(async fixture =>
        {
            var directory = Path.GetDirectoryName(fixture.ProjectPath)!;
            var sourceId = DocumentId.CreateNewId(fixture.Owner);
            var original = fixture.Solution.AddDocument(sourceId, "Source.cs", SourceText.From("class Source { }"), filePath: Path.Combine(directory, "Source.cs"));
            var configPath = Path.Combine(directory, ".editorconfig");
            const string text = "root = true\n[*.cs]\ndotnet_diagnostic.CS0168.severity = error\n";
            await File.WriteAllTextAsync(configPath, text);
            var modified = original.AddAnalyzerConfigDocument(DocumentId.CreateNewId(fixture.Owner), ".editorconfig", SourceText.From(text), filePath: configPath);
            var project = modified.GetProject(fixture.Owner)!;
            var tree = await project.GetDocument(sourceId)!.GetSyntaxTreeAsync();
            var provider = project.CompilationOptions!.SyntaxTreeOptionsProvider!;
            Assert.IsTrue(provider.TryGetDiagnosticValue(tree!, "CS0168", CancellationToken.None, out var severity));
            Assert.AreEqual(ReportDiagnostic.Error, severity, "The document delta must produce a real Roslyn diagnostic-option change.");
            var rendered = await RenderAsync(fixture, original, modified);
            var items = Evaluate(fixture.ProjectPath, rendered, "EditorConfigFiles");
            Assert.HasCount(1, items);
            Assert.AreEqual(configPath, items[0].Path);
            Assert.AreSame(provider, project.CompilationOptions.SyntaxTreeOptionsProvider, "Validation must not replace or discard the reviewed provider.");
            CollectionAssert.AreEqual(fixture.Before, await File.ReadAllBytesAsync(fixture.ProjectPath));
        });

    private static Solution AddDocument(Solution solution, string kind, DocumentId id, string name, string path)
    {
        var text = SourceText.From("// owned document");
        return kind switch
        {
            "Compile" => solution.AddDocument(id, name, text, ["Virtual;Folder%3B"], path),
            "AdditionalFiles" => solution.AddAdditionalDocument(id, name, text, filePath: path),
            "EditorConfigFiles" => solution.AddAnalyzerConfigDocument(id, name, text, filePath: path),
            _ => throw new AssertFailedException("Unknown document kind.")
        };
    }

    private static Solution RemoveDocument(Solution solution, string kind, DocumentId id) => kind switch
    {
        "Compile" => solution.RemoveDocument(id),
        "AdditionalFiles" => solution.RemoveAdditionalDocument(id),
        "EditorConfigFiles" => solution.RemoveAnalyzerConfigDocument(id),
        _ => throw new AssertFailedException("Unknown document kind.")
    };

    private static async Task<byte[]> RenderAsync(Fixture fixture, Solution original, Solution modified)
    {
        var owner = original.GetProject(fixture.Owner)!;
        var edges = owner.ProjectReferences.Select(reference => CompilerReferenceAuthority.Key(original, reference)).ToImmutableHashSet(StringComparer.Ordinal);
        var authority = new CompilerReferenceAuthority(new Dictionary<string, CompilerReferenceAuthority.ProjectEdges>(FileSystemPath.Comparer)
        {
            [PhysicalProjectIdentity.Key(owner, static path => path)] = new(edges, ImmutableHashSet<string>.Empty)
        });
        var mutations = await SolutionFileMutationPlanner.CreateAsync(original, modified,
            async (path, ct) => File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null,
            authority, includeDocuments: false, allowVirtualPaths: false, CancellationToken.None);
        return mutations.Single(mutation => FileSystemPath.Comparer.Equals(mutation.Path, fixture.ProjectPath)).After
            ?? throw new AssertFailedException("The renderer deleted the owning project.");
    }

    private sealed record Item(string Path, string Aliases, string EmbedInteropTypes, string Link);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Item[] Evaluate(string path, byte[] bytes, string kind)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = System.Xml.XmlReader.Create(stream);
        var root = Microsoft.Build.Construction.ProjectRootElement.Create(reader, collection);
        root.FullPath = path;
        try
        {
            var project = new Microsoft.Build.Evaluation.Project(root, null, null, collection);
            return project.GetItems(kind).Select(item => new Item(item.GetMetadataValue("FullPath"),
                item.GetMetadataValue("Aliases"), item.GetMetadataValue("EmbedInteropTypes"), item.GetMetadataValue("Link"))).ToArray();
        }
        finally { collection.UnloadAllProjects(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Escape(string value) => Microsoft.Build.Evaluation.ProjectCollection.Escape(value);

    private static async Task WithFixtureAsync(Func<Fixture, Task> inspect)
    {
        MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, nameof(SolutionFileMutationEscapingTests), Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "owner", "Owner.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        using var workspace = new AdhocWorkspace();
        var owner = ProjectId.CreateNewId();
        var original = workspace.CurrentSolution.AddProject(ProjectInfo.Create(owner, VersionStamp.Create(), "Owner", "Owner",
            LanguageNames.CSharp, filePath: project, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: CSharpParseOptions.Default));
        var before = Encoding.UTF8.GetBytes("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllBytesAsync(project, before);
        Exception? primary = null;
        try { await inspect(new Fixture(root, project, owner, original, before)); }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Literal mutation fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class Fixture(string root, string projectPath, ProjectId owner, Solution solution, byte[] before)
    {
        internal string Root { get; } = root;
        internal string ProjectPath { get; } = projectPath;
        internal ProjectId Owner { get; } = owner;
        internal Solution Solution { get; } = solution;
        internal byte[] Before { get; private set; } = before;

        internal async Task SetImportedItemAsync(string kind, string target)
        {
            var import = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "Owned.props");
            await File.WriteAllTextAsync(import, new XElement("Project", new XElement("ItemGroup",
                new XElement(kind, new XAttribute("Include", Escape(Path.GetRelativePath(Path.GetDirectoryName(ProjectPath)!, target)))))).ToString());
            Before = Encoding.UTF8.GetBytes("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"Owned.props\" /></Project>");
            await File.WriteAllBytesAsync(ProjectPath, Before);
        }
    }
}
