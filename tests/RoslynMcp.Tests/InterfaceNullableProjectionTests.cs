using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;
using static RoslynMcp.Tests.InterfaceNullableContextProjectionIntegrationTests;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceNullableProjectionTests
{
    private static readonly MetadataReference[] _references = [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];
    private static readonly CSharpParseOptions _parse = new(LanguageVersion.CSharp14);
    private const string _definitions = "public class Outer { public class Inner { } } public class Outer<T> { public class Inner<U> { } public class Middle<U> { public class Inner<V> { } } }";

    [TestMethod]
    [DataRow("Outer.Inner", "enable", false)]
    [DataRow("Outer<string>.Inner<object>", "enable", false)]
    [DataRow("Outer<string>.Middle<object>.Inner<string>", "enable", false)]
    [DataRow("Outer<string>.Inner<object>", "enable", true)]
    [DataRow("Outer<string>.Middle<object>.Inner<string>", "enable", true)]
    [DataRow("Outer<string>.Inner<object>", "disable", true)]
    [DataRow("Outer<string>.Middle<object>.Inner<string>", "disable", true)]
    public async Task Nullable_Projection_Distinguishes_Containing_Qualifiers_From_Contextual_Arguments(string type, string receivingPolicy, bool argumentMismatch)
    {
        var enabled = receivingPolicy == "enable";
        var options = Options(enabled);
        var sourceType = argumentMismatch ? type.Replace("Outer<string>", "Outer<\n#nullable " + (enabled ? "disable" : "enable") + " annotations\nstring\n#nullable restore annotations\n>", StringComparison.Ordinal) : type;
        var source = CSharpCompilation.Create("Source", [CSharpSyntaxTree.ParseText(_definitions + $" public class C {{ public {sourceType} M({sourceType} value) => value; }}", _parse)], _references, options);
        Assert.AreEqual(0, source.GetDiagnostics().Length, string.Join(Environment.NewLine, source.GetDiagnostics()));
        var member = source.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        using var workspace = new AdhocWorkspace();
        var project = Receiver(workspace, options, _parse).AddDocument("Definitions.cs", _definitions).Project;
        var text = $"public interface I {{ {type} M({type} value); }}";
        var document = project.AddDocument("IContract.cs", text);
        var raw = (await document.Project.GetCompilationAsync())!.GetTypeByMetadataName("I")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        Assert.AreEqual(member.ReturnNullableAnnotation, raw.ReturnNullableAnnotation, "The root must not trigger fallback in the argument-only control.");
        Assert.AreEqual(argumentMismatch, NullableGraph(member.ReturnType) != NullableGraph(raw.ReturnType));
        var expectedDifferences = argumentMismatch ? new[] { type.Contains("Middle", StringComparison.Ordinal) ? "type.parent.parent.arg0" : "type.parent.arg0" } : Array.Empty<string>();
        CollectionAssert.AreEqual(expectedDifferences, NullableDifferences(member.ReturnType, raw.ReturnType, "type").ToArray());
        CollectionAssert.AreEqual(expectedDifferences, NullableDifferences(member.Parameters[0].Type, raw.Parameters[0].Type, "type").ToArray());
        if (argumentMismatch)
        {
            var containing = ((INamedTypeSymbol)member.ReturnType).ContainingType!;
            while (containing.ContainingType is not null) containing = containing.ContainingType;
            Assert.AreEqual(enabled ? NullableAnnotation.None : NullableAnnotation.NotAnnotated, containing.TypeArguments[0].NullableAnnotation);
        }
        var result = await ProjectSingleAsync(document, member);
        var projectedText = (await result.GetTextAsync()).ToString();
        Assert.AreEqual(argumentMismatch, text != projectedText, projectedText);
        var serialized = CSharpCompilation.Create("Serialized", [CSharpSyntaxTree.ParseText(_definitions, _parse), CSharpSyntaxTree.ParseText(projectedText, _parse)], _references, options);
        Assert.AreEqual(0, serialized.GetDiagnostics().Length, string.Join(Environment.NewLine, serialized.GetDiagnostics()));
        var projected = serialized.GetTypeByMetadataName("I")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        Assert.AreEqual(NullableGraph(member.ReturnType), NullableGraph(projected.ReturnType));
        Assert.AreEqual(NullableGraph(member.Parameters[0].Type), NullableGraph(projected.Parameters[0].Type));
    }

    private static string NullableGraph(ITypeSymbol type) => type is INamedTypeSymbol named
        ? $"{type.Name}:{type.NullableAnnotation}[parent={(named.ContainingType is null ? "" : NullableGraph(named.ContainingType))};args={string.Join(";", named.TypeArguments.Select(NullableGraph))}]"
        : $"{type.Name}:{type.NullableAnnotation}";

    private static IEnumerable<string> NullableDifferences(ITypeSymbol source, ITypeSymbol receiving, string path)
    {
        if (source.NullableAnnotation != receiving.NullableAnnotation) yield return path;
        if (source is not INamedTypeSymbol original || receiving is not INamedTypeSymbol projected) yield break;
        if (original.ContainingType is not null && projected.ContainingType is not null)
            foreach (var difference in NullableDifferences(original.ContainingType, projected.ContainingType, path + ".parent")) yield return difference;
        for (var index = 0; index < original.TypeArguments.Length; index++)
            foreach (var difference in NullableDifferences(original.TypeArguments[index], projected.TypeArguments[index], path + ".arg" + index)) yield return difference;
    }

    [TestMethod]
    [DataRow("enable", "enable", "", false, "actual/IContract.cs", false)]
    [DataRow("disable", "disable", "", false, "actual/IContract.cs", false)]
    [DataRow("enable", "enable", "// <auto-generated/>\n", false, "actual/IContract.cs", true)]
    [DataRow("enable", "enable", "", true, "actual/IContract.cs", true)]
    [DataRow("enable", "enable", "", false, "actual/IContract.g.cs", true)]
    [DataRow("enable", "enable", "#nullable disable annotations\n", false, "actual/IContract.cs", true)]
    [DataRow("disable", "disable", "#nullable enable annotations\n", false, "actual/IContract.cs", true)]
    [DataRow("enable", "enable", "// <auto-generated/>\n#nullable enable annotations\n", true, "actual/IContract.cs", false)]
    [DataRow("enable", "disable", "#nullable enable annotations\n", false, "actual/IContract.cs", false)]
    [DataRow("disable", "enable", "#nullable disable annotations\n", false, "actual/IContract.cs", false)]
    public async Task Projection_Uses_Actual_Receiving_Tree_Context(string policy, string receivingPolicy, string header, bool generated, string path, bool requiresDecoration)
    {
        var enabled = policy == "enable";
        var signature = $"string{(enabled ? "?" : "")} Echo(string value)";
        var options = Options(enabled);
        var source = CSharpCompilation.Create("Source", [CSharpSyntaxTree.ParseText($"public class C {{ public {signature} => value; }}", _parse)], _references, options);
        var member = source.GetTypeByMetadataName("C")!.GetMembers("Echo").OfType<IMethodSymbol>().Single();
        using var workspace = new AdhocWorkspace();
        var project = Receiver(workspace, Options(receivingPolicy == "enable"), _parse);
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "interface-projection-context", Guid.NewGuid().ToString("N")));
        if (generated) project = project.AddAnalyzerConfigDocument(".editorconfig", SourceText.From("root = true\n[*.cs]\ngenerated_code = true\n"), filePath: Path.Combine(directory, ".editorconfig")).Project;
        var text = header + $"public interface I {{ {signature}; }}";
        var document = project.AddDocument("IContract.cs", text, filePath: Path.Combine(directory, path));
        if (generated)
        {
            var configured = (await document.Project.GetCompilationAsync())!;
            Assert.AreEqual(GeneratedKind.MarkedGenerated, configured.Options.SyntaxTreeOptionsProvider!.IsGenerated((await document.GetSyntaxTreeAsync())!, CancellationToken.None));
        }
        var result = await ProjectSingleAsync(document, member);
        var projectedText = (await result.GetTextAsync()).ToString();
        if (requiresDecoration) Assert.AreNotEqual(text, projectedText);
        else Assert.AreEqual(text, projectedText);
        result = result.Project.Solution.WithDocumentText(result.Id, SourceText.From(projectedText)).GetDocument(result.Id)!;
        var compilation = (await result.Project.GetCompilationAsync())!;
        Assert.IsFalse(compilation.GetDiagnostics().Any(d => d.Id is "CS8632" or "CS8669"), string.Join(Environment.NewLine, compilation.GetDiagnostics()));
        var projected = compilation.GetTypeByMetadataName("I")!.GetMembers("Echo").OfType<IMethodSymbol>().Single();
        Assert.AreEqual(member.ReturnNullableAnnotation, projected.ReturnNullableAnnotation);
        Assert.AreEqual(member.Parameters[0].NullableAnnotation, projected.Parameters[0].NullableAnnotation);
        Assert.AreEqual(options.GeneralDiagnosticOption, compilation.Options.GeneralDiagnosticOption);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Projection_Traverses_All_Raw_Type_Roles(bool enabled)
    {
        var sourceTree = CSharpSyntaxTree.ParseText(RawSource, _parse);
        var compilation = CSharpCompilation.Create("Source", [sourceTree, CSharpSyntaxTree.ParseText(_definitions, _parse)], _references, Options(true).WithAllowUnsafe(true));
        AssertNoErrors(compilation);
        var original = compilation.GetTypeByMetadataName("ISource")!;
        var declaration = sourceTree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();
        var projection = new InterfaceNullableProjection();
        var emitted = new List<MemberDeclarationSyntax>();
        var model = compilation.GetSemanticModel(sourceTree);
        foreach (var member in declaration.Members)
        {
            var symbol = member is EventFieldDeclarationSyntax evt ? model.GetDeclaredSymbol(evt.Declaration.Variables.Single())! : model.GetDeclaredSymbol(member)!;
            var clean = member.ReplaceTrivia(member.DescendantTrivia(descendIntoTrivia: true).Where(t => t.IsDirective), (_, _) => default);
            emitted.Add(projection.Capture(clean, symbol));
        }
        var root = SyntaxFactory.ParseCompilationUnit("using System; using System.Collections.Generic; " + _definitions)
            .AddMembers(declaration.WithIdentifier(SyntaxFactory.Identifier("I")).WithoutTrivia().WithMembers(SyntaxFactory.List(emitted))).NormalizeWhitespace();
        using var workspace = new AdhocWorkspace();
        var document = Receiver(workspace, Options(enabled).WithAllowUnsafe(true), _parse).AddDocument("I.cs", root.ToFullString());
        var undecorated = (await document.Project.GetCompilationAsync())!.GetTypeByMetadataName("I")!;
        Assert.IsTrue(original.GetMembers().Where(m => m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol)
            .Any(m => !Types(m).Select(Graph).SequenceEqual(Types(undecorated.GetMembers(m.Name).Single(x => x.Kind == m.Kind)).Select(Graph))), "No-decoration negative control must fail the graph oracle.");
        var solution = await projection.ApplyAsync(document, root, CancellationToken.None);
        var result = solution.GetDocument(document.Id)!;
        solution = solution.WithDocumentText(document.Id, SourceText.From((await result.GetTextAsync()).ToString()));
        var actual = (await solution.GetProject(document.Project.Id)!.GetCompilationAsync())!;
        AssertNoErrors(actual);
        var contract = actual.GetTypeByMetadataName("I")!;
        foreach (var member in original.GetMembers().Where(m => m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol))
        {
            var projected = contract.GetMembers(member.Name).Single(m => m.Kind == member.Kind);
            CollectionAssert.AreEqual(Types(member).Select(Graph).ToArray(), Types(projected).Select(Graph).ToArray(), member.Name);
            foreach (var pair in Types(member).Zip(Types(projected))) Assert.AreEqual(NullableGraph(pair.First), NullableGraph(pair.Second));
        }
        var beforeModel = (await document.GetSemanticModelAsync())!;
        var afterModel = (await solution.GetDocument(document.Id)!.GetSemanticModelAsync())!;
        Assert.AreEqual(beforeModel.GetNullableContext(0) & NullableContext.WarningsEnabled, afterModel.GetNullableContext(0) & NullableContext.WarningsEnabled);
        var beforeTokens = SyntaxFactory.ParseTokens((await document.GetTextAsync()).ToString()).ToArray();
        var afterTokens = SyntaxFactory.ParseTokens((await solution.GetDocument(document.Id)!.GetTextAsync()).ToString()).ToArray();
        CollectionAssert.AreEqual(beforeTokens.Select(t => (t.RawKind, t.Text)).ToArray(), afterTokens.Select(t => (t.RawKind, t.Text)).ToArray());
        for (var i = 0; i < beforeTokens.Length; i++)
            Assert.AreEqual(beforeModel.GetNullableContext(beforeTokens[i].SpanStart) & NullableContext.WarningsEnabled,
                afterModel.GetNullableContext(afterTokens[i].SpanStart) & NullableContext.WarningsEnabled, "Warning policy at lexical token " + i);
    }

    [TestMethod]
    public void Decoration_Diagnostic_Differential_Preserves_Identity_And_Multiplicity()
    {
        var tree = CSharpSyntaxTree.ParseText("#nullable enable annotations\nclass C { Missing M; }\n", path: "contract.cs");
        var descriptor = new DiagnosticDescriptor("TEST001", "title", "payload {0}", "test", DiagnosticSeverity.Error, true);
        var location = Location.Create(tree, new TextSpan(34, 1));
        var additional = Location.Create(tree, new TextSpan(42, 1));
        var first = Diagnostic.Create(descriptor, location, "first");
        var second = Diagnostic.Create(descriptor, location, "second");
        var withAdditional = Diagnostic.Create(descriptor, location, [additional], ImmutableDictionary<string, string?>.Empty, "first");
        var withProperty = Diagnostic.Create(descriptor, location, [], ImmutableDictionary<string, string?>.Empty.Add("key", "value"), "first");
        static string LocationKey(Location l, bool _) => l.SourceTree?.FilePath + ":" + l.SourceSpan;
        CollectionAssert.AreEqual(new[] { second, withAdditional, withProperty }, InterfaceNullableProjection.AddedErrors([first, first, first], [second, withAdditional, withProperty], LocationKey));
        CollectionAssert.AreEqual(new[] { first }, InterfaceNullableProjection.AddedErrors([first], [first, first], LocationKey));
        Assert.AreEqual(0, InterfaceNullableProjection.AddedErrors([first, first], [first], LocationKey).Length);
        Assert.AreEqual(1, new[] { first, second, withAdditional, withProperty }.Select(d => d.Id).Distinct().Count(), "IDs-only mutant loses the four identities above.");
        Assert.AreEqual(1, new[] { first, first }.Distinct().Count(), "Set mutant loses required occurrence consumption.");
    }

    [TestMethod]
    public void Lexical_Anchors_Preserve_Original_Trivia_Eof_Recovery_And_Cross_Line_Spans()
    {
        const string text = "#nullable enable annotations\ninterface I { string this[] { get; } Missing M(); } // tail\n";
        var position = text.IndexOf("string", StringComparison.Ordinal);
        var inserted = "\n#nullable disable annotations\n";
        var map = new InterfaceNullableProjection.TextProjection(text, [(position, inserted)], _parse);
        for (var i = 0; i <= text.Length; i++)
            Assert.AreEqual(map.Anchor(i, false, false), map.Anchor(map.Forward(i), true, false), "original start " + i);
        Assert.AreNotEqual(map.Anchor(position, false, false), map.Anchor(position + 2, true, false), "New directive cannot borrow an original token/trivia identity.");
        Assert.AreEqual(map.Anchor(position, false, true), map.Anchor(position, true, true), "End before inserted trivia retains original identity.");
        var baseline = CSharpCompilation.Create("Baseline", [CSharpSyntaxTree.ParseText(text, _parse)], _references, Options(true));
        var decorated = CSharpCompilation.Create("Decorated", [CSharpSyntaxTree.ParseText(map.Text, _parse)], _references, Options(true));
        var added = InterfaceNullableProjection.AddedErrors(baseline.GetDiagnostics(), decorated.GetDiagnostics(),
            (l, after) => map.Anchor(l.SourceSpan.Start, after, false) + "|" + map.Anchor(l.SourceSpan.End, after, true));
        Assert.AreEqual(0, added.Length, string.Join(Environment.NewLine, added.Select(d => d.ToString())));
    }

    [TestMethod]
    public async Task Construction_Rejects_Missing_Arity_And_Member_Mapping_Before_Serialization()
    {
        var source = CSharpCompilation.Create("Source", [CSharpSyntaxTree.ParseText("public interface S { string M(string value); }")], _references, Options(true));
        var member = source.GetTypeByMetadataName("S")!.GetMembers("M").Single();
        Assert.ThrowsExactly<InvalidOperationException>(() => new InterfaceNullableProjection().Capture(SyntaxFactory.ParseMemberDeclaration("string M();")!, member));
        Assert.ThrowsExactly<InvalidOperationException>(() => new InterfaceNullableProjection().Capture(SyntaxFactory.ParseMemberDeclaration("string P { get; }")!, member));
        var projection = new InterfaceNullableProjection();
        projection.Capture(SyntaxFactory.ParseMemberDeclaration("string M(string value);")!, member);
        var root = SyntaxFactory.ParseCompilationUnit("interface I { string M(string value); }");
        using var workspace = new AdhocWorkspace();
        var document = Receiver(workspace, Options(false), _parse).AddDocument("I.cs", root.ToFullString());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => projection.ApplyAsync(document, root, CancellationToken.None));
    }

    private static async Task<Document> ProjectSingleAsync(Document document, ISymbol symbol)
    {
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync())!;
        var member = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single().Members.Single();
        var projection = new InterfaceNullableProjection();
        root = root.ReplaceNode(member, projection.Capture(member, symbol));
        var solution = await projection.ApplyAsync(document, root, CancellationToken.None);
        return solution.GetDocument(document.Id)!;
    }

    [TestMethod]
    [DataRow("unbound")]
    [DataRow("unsafe")]
    [DataRow("keyword")]
    public async Task Decoration_Preserves_Actual_Legacy_Error_Payloads(string specimen)
    {
        var sourceText = specimen switch
        {
            "unbound" => "public class C { public string M(Missing value) => throw new System.Exception(); }",
            "unsafe" => "public unsafe class C { public string M(int* value) => throw new System.Exception(); }",
            _ => "public class C { public string @event() => throw new System.Exception(); }",
        };
        var source = CSharpCompilation.Create("Source", [CSharpSyntaxTree.ParseText(sourceText, _parse)], _references, Options(true).WithAllowUnsafe(true));
        var member = source.GetTypeByMetadataName("C")!.GetMembers().OfType<IMethodSymbol>().Single(m => m.MethodKind == MethodKind.Ordinary);
        var syntax = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)), member.Name)
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(member.Parameters.Select(p =>
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name)).WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        var projection = new InterfaceNullableProjection();
        var root = SyntaxFactory.CompilationUnit().AddMembers(SyntaxFactory.InterfaceDeclaration("I").AddMembers(projection.Capture(syntax, member))).NormalizeWhitespace();
        using var workspace = new AdhocWorkspace();
        var document = Receiver(workspace, Options(false), _parse).AddDocument("I.cs", root.ToFullString());
        var before = (await document.Project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsTrue(before.Length > 0, specimen);
        var solution = await projection.ApplyAsync(document, root, CancellationToken.None);
        var result = solution.GetDocument(document.Id)!;
        Assert.AreNotEqual(root.ToFullString(), (await result.GetTextAsync()).ToString());
        var after = (await result.Project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        CollectionAssert.AreEqual(before.Select(d => (d.Id, d.GetMessage())).OrderBy(d => d.Id).ToArray(), after.Select(d => (d.Id, d.GetMessage())).OrderBy(d => d.Id).ToArray());
    }

    [TestMethod]
    public async Task Old_Language_Distinguishes_New_Directive_Errors_From_Original_Identical_Ids()
    {
        var source = CSharpCompilation.Create("Source", [CSharpSyntaxTree.ParseText("public interface S { string M(string value); }")], _references, Options(true));
        var member = source.GetTypeByMetadataName("S")!.GetMembers("M").Single();
        using var workspace = new AdhocWorkspace();
        var document = Receiver(workspace, Options(false), new CSharpParseOptions(LanguageVersion.CSharp7_3))
            .AddDocument("I.cs", "#nullable disable annotations\npublic interface I { string M(string value); }");
        Assert.IsTrue((await document.Project.GetCompilationAsync())!.GetDiagnostics().Any(d => d.Id == "CS8370"));
        var error = await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => ProjectSingleAsync(document, member));
        StringAssert.Contains(error.Message, "CS8370");
    }

    private static CSharpCompilationOptions Options(bool enabled) => new(OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: enabled ? NullableContextOptions.Enable : NullableContextOptions.Disable);

    private static Project Receiver(AdhocWorkspace workspace, CSharpCompilationOptions options, CSharpParseOptions parse) =>
        workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Receiver", "Receiver", LanguageNames.CSharp,
            compilationOptions: options, parseOptions: parse, metadataReferences: _references));

    private static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }

    private const string RawSource = """
        using System;
        using System.Collections.Generic;
        public unsafe interface ISource {
        #nullable disable annotations
            string Oblivious(string value);
        #nullable enable annotations
            ref string Ref(ref string value);
            ref readonly string Read(in string value);
            string? this[string? key, List<string[]> value] { get; }
            event Action<string?>? Changed;
            delegate*<string?, string> Function(delegate*<string, string?> value);
            int* Pointer(int* value);
            (string? name, List<string[]>? values)? Tuple((string? name, List<string[]>? values)? value);
            string[][,]? Array(string[][,]? value);
            string
        #nullable disable annotations
            []
        #nullable enable annotations
            [] Ranked(string
        #nullable disable annotations
            []
        #nullable enable annotations
            [] value);
            global::Outer<string>.Middle<object>.Inner<string> Qualified(global::Outer<string>.Middle<object>.Inner<string> value);
            List<
        #nullable disable annotations
                string
        #nullable enable annotations
            > Mixed(List<
        #nullable disable annotations
                string
        #nullable enable annotations
            > value);
        }
        """;
}
