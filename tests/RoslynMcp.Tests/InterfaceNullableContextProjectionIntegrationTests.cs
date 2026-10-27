using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceNullableContextProjectionIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    public static IEnumerable<object[]> ReceivingCases()
    {
        foreach (var route in new[] { "standalone", "cross-project", "dependency-inversion", "extract-and-wire" })
            foreach (var enabled in new[] { false, true })
                foreach (var sourceEnabled in route == "standalone" ? new[] { enabled } : new[] { false, true })
                    yield return [route, enabled, sourceEnabled];
    }

    public static IEnumerable<object[]> LegacyCases()
    {
        foreach (var route in new[] { "standalone", "cross-project" })
            foreach (var enabled in new[] { false, true })
                foreach (var recursive in new[] { false, true }) yield return [route, enabled, recursive];
    }

    [TestMethod]
    [DynamicData(nameof(LegacyCases))]
    public async Task Legacy_Indexer_Recovery_Preserves_All_Actually_Emitted_Type_Slots(string route, bool enabled, bool recursive)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var receiver = route == "standalone" ? "SampleLib" : "Contracts";
        SetPolicy(workspace.GetPath(receiver, receiver + ".csproj"), enabled, "14.0");
        var type = recursive ? "System.Collections.Generic.List<(string, string[])[]>" : "string";
        var source = "#nullable " + (enabled ? "disable" : "enable") + " annotations\nnamespace Contracts { public class NullableService { public " + type +
            " this[int key] { get => throw new System.NotImplementedException(); } } }";
        var path = workspace.GetPath("SampleLib", "NullableService.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertPolicyAsync(workspace.WorkspaceId, receiver, enabled, LanguageVersion.CSharp14, route == "standalone" ? enabled : true);
        await AssertCompilesAsync(before);
        var files = Snapshot(workspace.RootPath);
        var preview = await PreviewAsync(route, workspace.WorkspaceId, path, new CompositePreviewStore());
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var document = pending.Projects.Single(p => p.Name == receiver).Documents.Single(d => d.Name == "INullableService.cs");
        var text = (await document.GetTextAsync()).ToString();
        StringAssert.Contains(text, "this[]");
        Assert.IsFalse(text.Contains("int key", StringComparison.Ordinal), "Do not invent legacy omitted indexer keys.");
        var tree = (await document.GetSyntaxTreeAsync())!;
        var model = (await document.GetSemanticModelAsync())!;
        var tokens = SyntaxFactory.ParseTokens(text, options: (CSharpParseOptions)document.Project.ParseOptions!).ToArray();
        var contextual = tokens.Where(t => t.IsKind(SyntaxKind.StringKeyword) || t.IsKind(SyntaxKind.GreaterThanToken) ||
            (recursive && t.IsKind(SyntaxKind.CloseBracketToken) && t.SpanStart < text.IndexOf("this[]", StringComparison.Ordinal))).ToArray();
        Assert.AreEqual(recursive ? 5 : 1, contextual.Length, text);
        foreach (var token in contextual)
            Assert.AreEqual(!enabled, (model.GetNullableContext(token.SpanStart) & NullableContext.AnnotationsEnabled) != 0, text);
        // Preserve malformed old-emitter output as a differential control, never call it compilable.
        var baselineText = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^#nullable (enable|disable|restore) annotations\r?\n", "");
        var baseline = pending.WithDocumentText(document.Id, Microsoft.CodeAnalysis.Text.SourceText.From(baselineText));
        var baselineTree = await baseline.GetDocument(document.Id)!.GetSyntaxTreeAsync();
        var oldErrors = (await baseline.GetProject(document.Project.Id)!.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        var newErrors = (await document.Project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsTrue(oldErrors.Length > 0, "Control must retain the original malformed indexer.");
        string LocationKey(Location location, bool after)
        {
            if (location.SourceTree != (after ? tree : baselineTree))
                return location.SourceTree?.FilePath + ":" + location.SourceSpan;
            var lexical = SyntaxFactory.ParseTokens(after ? text : baselineText).ToArray();
            string Anchor(int position, bool end)
            {
                if (position == (after ? text : baselineText).Length) return "eof";
                var pair = lexical.Select((t, i) => (t, i)).First(p => end ? p.t.Span.End >= position : p.t.Span.End > position);
                return pair.i + ":" + (position - pair.t.SpanStart);
            }
            return "interface:" + Anchor(location.SourceSpan.Start, false) + "|" + Anchor(location.SourceSpan.End, true);
        }
        string Identity(Diagnostic diagnostic, bool after) => System.Text.Json.JsonSerializer.Serialize(new
        {
            diagnostic.Id,
            diagnostic.Severity,
            diagnostic.DefaultSeverity,
            diagnostic.IsWarningAsError,
            Message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            Properties = diagnostic.Properties.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            Location = LocationKey(diagnostic.Location, after),
            Additional = diagnostic.AdditionalLocations.Select(l => LocationKey(l, after)).ToArray(),
        });
        var remaining = oldErrors.Select(d => Identity(d, false)).ToList();
        foreach (var diagnostic in newErrors) Assert.IsTrue(remaining.Remove(Identity(diagnostic, true)), diagnostic.ToString());
        AssertSnapshot(files);
    }

    [TestMethod]
    [DynamicData(nameof(ReceivingCases))]
    public async Task Receiving_Context_Preserves_Emitted_Nullable_States(string route, bool enabled, bool sourceEnabled)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        var receivingName = route == "standalone" ? "SampleLib" : "Contracts";
        SetPolicy(workspace.GetPath("SampleLib", "SampleLib.csproj"), sourceEnabled, "14.0");
        SetPolicy(workspace.GetPath(receivingName, receivingName + ".csproj"), enabled, "14.0");
        var path = workspace.GetPath("SampleLib", "NullableService.cs");
        await File.WriteAllTextAsync(path, Source + "\n" + EventSource + "\n}\n}");
        var files = Snapshot(workspace.RootPath);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertPolicyAsync(workspace.WorkspaceId, receivingName, enabled, LanguageVersion.CSharp14, sourceEnabled);
        await AssertCompilesAsync(before);
        await AssertRuntimeAsync(before, false, receivingName, false);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var stores = new CompositePreviewStore();
        var preview = await PreviewAsync(route, workspace.WorkspaceId, path, stores);
        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, stores.Retrieve(preview.PreviewToken)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        await AssertGraphsAsync(before, pending, receivingName);
        var emitted = pending.Projects.Single(p => p.Name == receivingName).Documents.Single(d => d.Name == "INullableService.cs");
        var emittedText = (await emitted.GetTextAsync()).ToString();
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(emittedText, "(?<!\r)\n"), "Directives must use the document's CRLF line endings:\n" + emittedText);
        Assert.IsTrue(emittedText.Split("\r\n").Where(l => l.Contains("#nullable", StringComparison.Ordinal)).All(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^#nullable (enable|disable) annotations$")), emittedText);
        // Negative control: the same route output without its decoration must fail the graph oracle.
        var undecorated = pending.WithDocumentText(emitted.Id, Microsoft.CodeAnalysis.Text.SourceText.From(
            System.Text.RegularExpressions.Regex.Replace(emittedText, "(?m)^#nullable (enable|disable) annotations\r\n", "")));
        Assert.AreNotEqual(emittedText, (await undecorated.GetDocument(emitted.Id)!.GetTextAsync()).ToString());
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() => AssertGraphsAsync(before, undecorated, receivingName));
        await AssertCompilesAsync(pending);
        await AssertRuntimeAsync(pending, true, receivingName, route == "dependency-inversion");
        AssertSnapshot(files);
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        var target = pending.Projects.Single(p => p.Name == receivingName).Documents.Single(d => d.Name == "INullableService.cs");
        Assert.IsFalse(File.Exists(target.FilePath));
        var applied = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, stores, undoService: UndoService).ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "nullable_context", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await workspace.ReloadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, receivingName, enabled, LanguageVersion.CSharp14, sourceEnabled);
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertGraphsAsync(before, reloaded, receivingName);
        await AssertCompilesAsync(reloaded);
        await AssertRuntimeAsync(reloaded, true, receivingName, route == "dependency-inversion");
        Assert.AreEqual((await target.GetTextAsync()).ToString(), await File.ReadAllTextAsync(target.FilePath!));
        foreach (var (file, bytes) in files.Where(f => !f.Key.EndsWith("NullableService.cs", StringComparison.Ordinal) && !f.Key.EndsWith("SampleLib.csproj", StringComparison.Ordinal)))
        {
            if (route == "dependency-inversion" && file == workspace.GetPath("Consumer", "NativeConsumer.cs"))
            {
                var expectedDocument = pending.Projects.Single(p => p.Name == "Consumer").Documents.Single(d => d.FilePath == file);
                CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes((await expectedDocument.GetTextAsync()).ToString()), File.ReadAllBytes(file), file);
            }
            else CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file), file);
        }
    }

    [TestMethod]
    [DataRow("cross-project", false)]
    [DataRow("dependency-inversion", false)]
    [DataRow("extract-and-wire", false)]
    [DataRow("cross-project", true)]
    [DataRow("dependency-inversion", true)]
    [DataRow("extract-and-wire", true)]
    public async Task Old_Receiving_Language_Rejects_Only_New_Nullable_Decoration(string route, bool oblivious)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        PrepareFixture(workspace);
        SetPolicy(workspace.GetPath("Contracts", "Contracts.csproj"), false, "7.3");
        var path = workspace.GetPath("SampleLib", "NullableService.cs");
        await File.WriteAllTextAsync(path, "#nullable " + (oblivious ? "disable" : "enable") + " annotations\nnamespace Contracts { public class NullableService { public string Echo(string value) => value; } }");
        var files = Snapshot(workspace.RootPath);
        await workspace.LoadAsync();
        await AssertPolicyAsync(workspace.WorkspaceId, "Contracts", false, LanguageVersion.CSharp7_3, true);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var ordinaryCount = ((Helpers.TrackingPreviewStore)PreviewStore).GetRedeemableTokens(workspace.WorkspaceId).Count;
        var stores = new CompositePreviewStore();
        if (oblivious)
        {
            var preview = await PreviewAsync(route, workspace.WorkspaceId, path, stores);
            var pending = route == "extract-and-wire" ? SerializedCompositePreview(before, stores.Retrieve(preview.PreviewToken)!.Value.Mutations)
                : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            await AssertCompilesAsync(await ReparseSerializedDocumentsAsync(pending));
            var text = (await pending.Projects.Single(p => p.Name == "Contracts").Documents.Single(d => d.Name == "INullableService.cs").GetTextAsync()).ToString();
            Assert.IsFalse(text.Contains("#nullable", StringComparison.Ordinal));
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PreviewAsync(route, workspace.WorkspaceId, path, stores));
            StringAssert.Contains(error.Message, "CS8370");
            StringAssert.Contains(error.Message, "receiving compiler policy");
            Assert.IsFalse(error.Message.Contains(path, StringComparison.Ordinal));
            Assert.AreEqual(ordinaryCount, ((Helpers.TrackingPreviewStore)PreviewStore).GetRedeemableTokens(workspace.WorkspaceId).Count);
            Assert.AreEqual(0, StoreCount(stores));
        }
        AssertSnapshot(files);
        Assert.IsFalse(File.Exists(workspace.GetPath("Contracts", "INullableService.cs")));
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
    }

    private static Task<RefactoringPreviewDto> PreviewAsync(string route, string id, string path, CompositePreviewStore stores) => route switch
    {
        "standalone" => InterfaceExtractionService.PreviewExtractInterfaceAsync(id, path, "NullableService", "INullableService", null, false, CancellationToken.None),
        "cross-project" => CrossProjectRefactoringService.PreviewExtractInterfaceAsync(id, path, "NullableService", "INullableService", "Contracts", CancellationToken.None),
        "dependency-inversion" => CrossProjectRefactoringService.PreviewDependencyInversionAsync(id, path, "NullableService", "INullableService", "Contracts", CancellationToken.None),
        "extract-and-wire" => new ExtractAndWireOrchestrator(WorkspaceManager, stores, PreviewStore, CrossProjectRefactoringService, DiRegistrationService)
            .PreviewExtractAndWireInterfaceAsync(id, path, "NullableService", "INullableService", "Contracts", true, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static void PrepareFixture(IsolatedWorkspaceScope workspace)
    {
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        AddProjectToCopiedSolution(workspace.RootPath, "Consumer", "net10.0");
        var solution = XDocument.Load(workspace.SolutionPath);
        foreach (var project in solution.Descendants("Project").Where(p => !new[] { "SampleLib/SampleLib.csproj", "Contracts/Contracts.csproj", "Consumer/Consumer.csproj" }.Contains(p.Attribute("Path")!.Value.Replace('\\', '/'))).ToArray()) project.Remove();
        solution.Save(workspace.SolutionPath);
        var path = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Load(path);
        xml.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultCompileItems", "false")),
            new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "NullableService.cs"))));
        xml.Save(path);
        SetPolicy(path, true, "14.0");
        var consumerPath = workspace.GetPath("Consumer", "Consumer.csproj");
        var consumer = XDocument.Load(consumerPath);
        consumer.Root!.Add(new XElement("ItemGroup",
            new XElement("ProjectReference", new XAttribute("Include", "../SampleLib/SampleLib.csproj")),
            new XElement("ProjectReference", new XAttribute("Include", "../Contracts/Contracts.csproj"))));
        consumer.Save(consumerPath);
        SetPolicy(consumerPath, true, "14.0");
        File.WriteAllText(workspace.GetPath("Consumer", "NativeConsumer.cs"),
            "namespace Contracts { public class NativeConsumer { public NativeConsumer(NullableService service) { _ = service.GetType(); } } }");
    }

    private static int StoreCount(object store)
    {
        var type = store.GetType().BaseType!;
        var entries = (System.Collections.IDictionary)type.GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        return entries.Count;
    }

    private static void SetPolicy(string path, bool enabled, string language)
    {
        var xml = XDocument.Load(path);
        xml.Root!.Add(new XElement("PropertyGroup", new XElement("Nullable", enabled ? "enable" : "disable"),
            new XElement("LangVersion", language), new XElement("ImplicitUsings", Path.GetFileName(path) == "SampleLib.csproj" ? "enable" : "disable"), new XElement("TreatWarningsAsErrors", "true")));
        xml.Save(path);
    }

    private static async Task AssertPolicyAsync(string id, string receiver, bool enabled, LanguageVersion language, bool sourceEnabled)
    {
        var errors = WorkspaceManager.GetStatus(id).WorkspaceDiagnostics.Where(d => d.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.Message)));
        foreach (var project in WorkspaceManager.GetCurrentSolution(id).Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            Assert.AreEqual((project.Name == receiver ? enabled : project.Name == "SampleLib" ? sourceEnabled : true) ? NullableContextOptions.Enable : NullableContextOptions.Disable, compilation.Options.NullableContextOptions, project.Name);
            if (project.Name == receiver)
            {
                Assert.AreEqual(language, ((CSharpParseOptions)project.ParseOptions!).LanguageVersion);
                Assert.AreEqual(ReportDiagnostic.Error, compilation.Options.GeneralDiagnosticOption);
            }
            if (project.Name == "Consumer")
            {
                Assert.AreEqual(LanguageVersion.CSharp14, ((CSharpParseOptions)project.ParseOptions!).LanguageVersion);
                Assert.AreEqual(ReportDiagnostic.Error, compilation.Options.GeneralDiagnosticOption);
                CollectionAssert.AreEquivalent(new[] { "Contracts", "SampleLib" }, project.ProjectReferences.Select(r => project.Solution.GetProject(r.ProjectId)!.Name).ToArray());
            }
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal))
        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(p => p, File.ReadAllBytes);

    private static void AssertSnapshot(Dictionary<string, byte[]> files)
    {
        foreach (var (file, bytes) in files) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file), file);
    }

    private static async Task AssertGraphsAsync(Solution source, Solution target, string receiver)
    {
        var original = (await source.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.GetTypeByMetadataName("Contracts.NullableService")!;
        var projected = (await target.Projects.Single(p => p.Name == receiver).GetCompilationAsync())!.GetTypeByMetadataName("Contracts.INullableService")!;
        var actualMembers = projected.GetMembers().Where(m => !m.IsImplicitlyDeclared && m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol).ToArray();
        var expectedMembers = original.GetMembers().Where(m => !m.IsImplicitlyDeclared && m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol)
            .Where(m => receiver == "SampleLib" || m is not IEventSymbol).ToArray();
        CollectionAssert.AreEqual(expectedMembers.Select(m => m.Kind + ":" + m.Name).Order(StringComparer.Ordinal).ToArray(),
            actualMembers.Select(m => m.Kind + ":" + m.Name).Order(StringComparer.Ordinal).ToArray());
        foreach (var member in actualMembers)
        {
            var expected = original.GetMembers(member.Name).Single(m => m.Kind == member.Kind);
            CollectionAssert.AreEqual(Types(expected).Select(Graph).ToArray(), Types(member).Select(Graph).ToArray(), member.Name);
        }
    }

    internal static IEnumerable<ITypeSymbol> Types(ISymbol member) => member switch
    {
        IMethodSymbol method => new[] { method.ReturnType }.Concat(method.Parameters.Select(p => p.Type)),
        IPropertySymbol property => new[] { property.Type }.Concat(property.Parameters.Select(p => p.Type)),
        IEventSymbol evt => [evt.Type],
        _ => [],
    };

    internal static string Graph(ITypeSymbol type) => type.NullableAnnotation + ":" + (type switch
    {
        IArrayTypeSymbol array => "array" + array.Rank + "[" + Graph(array.ElementType) + "]",
        IPointerTypeSymbol pointer => "pointer[" + Graph(pointer.PointedAtType) + "]",
        IFunctionPointerTypeSymbol function => "function[" + string.Join(";", function.Signature.Parameters.Select(p => Graph(p.Type)).Append(Graph(function.Signature.ReturnType))) + "]",
        INamedTypeSymbol named => named.Name + "[" + string.Join(";", named.TypeArguments.Select(Graph)) + "]",
        _ => type.Name,
    });

    private static async Task AssertRuntimeAsync(Solution solution, bool contract, string receiver, bool inverted)
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("nullable_" + Guid.NewGuid().ToString("N"), true);
        try
        {
            var assemblies = new Dictionary<string, System.Reflection.Assembly>();
            context.Resolving += (_, name) => assemblies.GetValueOrDefault(name.Name!);
            foreach (var name in new[] { "Contracts", "SampleLib", "Consumer" })
            {
                var compilation = (await solution.Projects.Single(p => p.Name == name).GetCompilationAsync())!;
                using var bytes = new MemoryStream();
                var result = compilation.Emit(bytes);
                Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
                bytes.Position = 0;
                assemblies.Add(name, context.LoadFromStream(bytes));
            }
            var concrete = assemblies["SampleLib"].GetType("Contracts.NullableService")!;
            var instance = Activator.CreateInstance(concrete)!;
            var method = contract ? assemblies[receiver].GetType("Contracts.INullableService")!.GetMethod("Echo")! : concrete.GetMethod("Echo")!;
            if (contract) Assert.IsTrue(method.DeclaringType!.IsAssignableFrom(concrete));
            Assert.AreEqual("hello", method.Invoke(instance, ["hello"]));
            var consumer = assemblies["Consumer"].GetType("Contracts.NativeConsumer")!;
            var constructor = consumer.GetConstructors().Single();
            Assert.AreEqual(inverted ? "Contracts.INullableService" : "Contracts.NullableService", constructor.GetParameters().Single().ParameterType.FullName);
            Assert.IsNotNull(constructor.Invoke([instance]));
        }
        finally { context.Unload(); }
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        namespace Contracts {
        public class NullableService {
            private int dropped;
            public NullableService() { }
        #nullable disable annotations
            public string Echo(string value) => value;
        #nullable enable annotations
            public string Plain(string value) => value;
            public string? Maybe(string? value) => value;
            public List<
        #nullable disable annotations
                string
        #nullable enable annotations
            > Nested(List<
        #nullable disable annotations
                string
        #nullable enable annotations
            > value) => value;
            public (string?, List<string[]>?)[]? Items { get; set; }
            public int Value { get => dropped; set => dropped = value; }
        """;

    private const string EventSource = """
        #nullable enable annotations
            public event Action<string?>? Changed { add { } remove { } }
        #nullable disable annotations
            public string AfterEvent(string value) => value;
        """;
}
