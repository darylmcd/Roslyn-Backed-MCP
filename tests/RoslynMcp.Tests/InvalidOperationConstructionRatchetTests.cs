using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>Pin existing unmarked constructions by semantic member and token identity, not count.</summary>
[TestClass]
public sealed class InvalidOperationConstructionRatchetTests
{
    private const string _factoryPath = "src/RoslynMcp.Core/Services/InvalidOperationErrors.cs";
    private const string _factoryMember = "M:RoslynMcp.Core.Services.InvalidOperationErrors.Internal(System.String,System.Exception)";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    public TestContext TestContext { get; set; } = null!;

    public sealed record Construction(string Path, string Member, string Fingerprint, int Count);
    private sealed record Site(string Path, string Member, string Fingerprint, int Offset);

    [TestMethod]
    [TestCategory("RepoSolution")]
    public async Task ProductionConstructions_DoNotGrowOrReplacePinnedIdentitiesAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var inventory = new List<Site>();
        // Production loading supplies real MSBuild parse options, source documents and references.
        // Separate owned containers avoid mutating the assembly-shared sample workspace.
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            using var services = TestServiceContainer.Create(new ValidationServiceOptions());
            var status = await services.WorkspaceManager.LoadAsync(
                Path.Combine(root, "RoslynMcp.slnx"),
                new Dictionary<string, string> { ["Configuration"] = configuration },
                CancellationToken.None);
            var solution = services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var constructions = new List<Site>();
            var factoryCount = 0;
            foreach (var project in solution.Projects.Where(project =>
                         project.FilePath is not null && Relative(root, project.FilePath).StartsWith("src/", StringComparison.Ordinal)))
            {
                var compilation = await project.GetCompilationAsync();
                Assert.IsNotNull(compilation);
                foreach (var tree in compilation.SyntaxTrees.Where(tree => IsProductionSource(root, tree.FilePath)))
                {
                    var path = Relative(root, tree.FilePath);
                    Assert.IsTrue(paths.Add(path), $"Duplicate source document: {path}");
                    var model = compilation.GetSemanticModel(tree);
                    var found = ScanSites(model, path);
                    foreach (var construction in found)
                    {
                        if (construction.Path == _factoryPath && construction.Member == _factoryMember)
                        {
                            factoryCount++;
                            Assert.AreEqual(Fingerprint(CSharpSyntaxTree.ParseText(
                                "new InvalidOperationException(serverDetail, inner)").GetRoot()
                                .DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single()),
                                construction.Fingerprint, "Factory must remain an exact, direct BCL construction.");
                        }
                        else
                        {
                            constructions.Add(construction);
                        }
                    }
                }
            }

            CollectionAssert.AreEquivalent(
                Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                    .Where(path => IsProductionSource(root, path)).Select(path => Relative(root, path)).ToArray(),
                paths.ToArray(), "Every production source file must belong to the semantic inventory.");
            Assert.AreEqual(1, factoryCount, "Only the factory's exact member may construct a new plain exception.");
            // Retain offsets only until configurations are unioned; persisted identities never include positions.
            inventory.AddRange(constructions);
        }

        var actual = AggregateSites(inventory);
        TestContext.WriteLine("INVENTORY-BEGIN" + JsonSerializer.Serialize(actual, _jsonOptions) + "INVENTORY-END");
        var baseline = JsonSerializer.Deserialize<Construction[]>(File.ReadAllText(
            Path.Combine(root, "tests", "RoslynMcp.Tests", "TestData", "invalid-operation-construction-baseline.json")));
        Assert.IsNotNull(baseline);
        Assert.IsTrue(baseline.Length > 0, "The committed debt inventory must be populated.");
        Assert.IsTrue(baseline.All(item => item.Count > 0 && item.Path.StartsWith("src/", StringComparison.Ordinal)
            && !item.Path.Contains("..", StringComparison.Ordinal) && !item.Path.Contains('\\')
            && item.Member.Length > 0 && item.Fingerprint.Length == 64));
        Assert.AreEqual(baseline.Length, baseline.Select(item => (item.Path, item.Member, item.Fingerprint)).Distinct().Count());
        AssertSubset(actual, baseline);
    }

    [TestMethod]
    [DataRow("throw new InvalidOperationException(\"x\");", true)]
    [DataRow("throw new System.InvalidOperationException(\"x\");", true)]
    [DataRow("throw new global::System.InvalidOperationException(\"x\");", true)]
    [DataRow("throw new /*comment*/ InvalidOperationException ( \"x\" );", true)]
    [DataRow("InvalidOperationException e = new(\"x\");", true)]
    [DataRow("throw new IOE(\"x\");", true)]
    [DataRow("throw new PublicInvalidOperationException(\"x\");", false)]
    [DataRow("// new InvalidOperationException(\"x\")\nreturn;", false)]
    [DataRow("var s = \"new InvalidOperationException(x)\";", false)]
    [DataRow("InvalidOperationException e = null;", false)]
    [DataRow("throw new Other.InvalidOperationException(\"x\");", false)]
    [DataRow("throw InvalidOperationErrors.Internal(\"x\");", false)]
    [DataRow("InvalidOperationException Local() => new(\"x\"); throw Local();", true)]
    [DataRow("throw new IOE();", true)]
    public void Corpus_ResolvesExactConstructorIdentity(string body, bool forbidden)
    {
        var actual = ScanSnippet(body);
        Assert.AreEqual(forbidden ? 1 : 0, actual.Sum(item => item.Count), body);
        if (forbidden)
        {
            Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(actual, []), body);
        }
        else
        {
            AssertSubset(actual, []);
        }
    }

    [TestMethod]
    public void IdentityRatchet_RejectsReplacementNewFileAndDuplicateGrowthButAllowsDeletionAndTrivia()
    {
        var baseline = ScanSnippet("throw new InvalidOperationException(\"old\");");
        AssertSubset(ScanSnippet("throw new /* trivia */ InvalidOperationException ( \"old\" );"), baseline);
        AssertSubset([], baseline);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("throw new InvalidOperationException(\"new\");"), baseline));
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("throw new InvalidOperationException(\"old\");", "src/new.cs"), baseline));
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("var a = new InvalidOperationException(\"old\"); var b = new InvalidOperationException(\"old\");"), baseline));
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("throw new InvalidOperationException(\"old\");", member: "OtherMember"), baseline));
    }

    [TestMethod]
    public void TopLevelConstructions_HaveCompilerResolvedMemberIdentity()
    {
        var tree = CSharpSyntaxTree.ParseText("throw new System.InvalidOperationException(\"x\");");
        var actual = Scan(CorpusModel(tree, OutputKind.ConsoleApplication), "src/Program.cs");
        Assert.AreEqual(1, actual.Single().Count);
        Assert.IsTrue(actual.Single().Member.StartsWith("M:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnresolvedConstructor_FailsClosed()
        => Assert.ThrowsExactly<AssertFailedException>(() => ScanSnippet("throw new MissingException(\"x\");"));

    [TestMethod]
    [DataRow("DEBUG")]
    [DataRow("RELEASE")]
    public void SupportedPreprocessorBranches_AndTargetTypedReturnAreInventoried(string symbol)
    {
        const string body = "#if DEBUG\nthrow new InvalidOperationException(\"debug\");\n#else\nInvalidOperationException Local() => new(\"release\"); throw Local();\n#endif";
        Assert.AreEqual(1, ScanSnippet(body, symbols: [symbol]).Sum(item => item.Count));
    }

    private static Construction[] ScanSnippet(string body, string path = "src/example.cs",
        string member = "Run", string[]? symbols = null)
    {
        return Scan(SnippetModel(body, path, member, symbols), path);
    }

    private static SemanticModel SnippetModel(string body, string path, string member, string[]? symbols)
    {
        var source = "using System; using IOE = System.InvalidOperationException; " +
            "class PublicInvalidOperationException : Exception { public PublicInvalidOperationException(string s) {} } " +
            "static class InvalidOperationErrors { public static Exception Internal(string s) => null; } " +
            "namespace Other { class InvalidOperationException : Exception { public InvalidOperationException(string s) {} } } " +
            $"class Sample {{ void {member}() {{\n{body}\n}} }}";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest,
            preprocessorSymbols: symbols ?? []), path);
        return CorpusModel(tree, OutputKind.DynamicallyLinkedLibrary);
    }

    private static SemanticModel CorpusModel(SyntaxTree tree, OutputKind outputKind)
        => CSharpCompilation.Create("Corpus", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(outputKind)).GetSemanticModel(tree);

    private static Construction[] Scan(SemanticModel model, string path) => AggregateSites(ScanSites(model, path));

    private static Site[] ScanSites(SemanticModel model, string path)
    {
        var objectType = model.Compilation.GetSpecialType(SpecialType.System_Object);
        Assert.AreNotEqual(TypeKind.Error, objectType.TypeKind, "The framework object type must resolve.");
        var coreAssembly = objectType.ContainingAssembly;
        Assert.IsFalse(SymbolEqualityComparer.Default.Equals(coreAssembly, model.Compilation.Assembly),
            "The framework object type must come from a metadata reference.");
        var exactType = coreAssembly.GetTypeByMetadataName("System.InvalidOperationException");
        Assert.IsNotNull(exactType, "The BCL InvalidOperationException symbol must resolve.");
        var identities = new List<Site>();
        foreach (var node in model.SyntaxTree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var constructor = model.GetSymbolInfo(node).Symbol as IMethodSymbol;
            Assert.IsNotNull(constructor, $"Unresolved construction at {path}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
            if (!SymbolEqualityComparer.Default.Equals(constructor.ContainingType, exactType))
            {
                continue;
            }

            var declaration = node.Ancestors().FirstOrDefault(ancestor =>
                ancestor is MemberDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax
                || ancestor is VariableDeclaratorSyntax variable && variable.Parent?.Parent is FieldDeclarationSyntax);
            Assert.IsNotNull(declaration, $"Construction has no containing member: {path}");
            var member = declaration is GlobalStatementSyntax
                ? model.GetEnclosingSymbol(node.SpanStart)
                : model.GetDeclaredSymbol(declaration);
            Assert.IsNotNull(member, $"Containing member did not resolve: {path}");
            identities.Add(new Site(path, MemberIdentity(member), Fingerprint(node), node.SpanStart));
        }

        return identities.ToArray();
    }

    private static string MemberIdentity(ISymbol member)
    {
        if (member is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
        {
            // Roslyn can emit a documentation ID for a local function without its lexical owner.
            // Preserve the entire containing-member chain, including nested local functions.
            Assert.IsNotNull(member.ContainingSymbol);
            return MemberIdentity(member.ContainingSymbol) + "/" +
                member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        var documentationId = member.GetDocumentationCommentId();
        Assert.IsNotNull(documentationId, $"Member needs a stable identity: {member}");
        return documentationId;
    }

    [TestMethod]
    public void NestedLocalFunctions_RetainEveryContainingMemberIdentity()
    {
        var baseline = ScanSnippet("void Outer() { void Inner() { throw new InvalidOperationException(\"x\"); } }");
        Assert.AreEqual(1, baseline.Single().Count);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("void Changed() { void Inner() { throw new InvalidOperationException(\"x\"); } }"), baseline));
    }

    [TestMethod]
    [DataRow("get", "set")]
    [DataRow("get", "init")]
    [DataRow("add", "remove")]
    public void AccessorBodies_HaveDistinctSemanticMemberIdentity(string from, string to)
    {
        var baseline = ScanAccessor(from, to, move: false);
        var moved = ScanAccessor(from, to, move: true);
        Assert.AreEqual(1, baseline.Single().Count);
        Assert.AreEqual(1, moved.Single().Count);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(moved, baseline));
    }

    private static Construction[] ScanAccessor(string from, string to, bool move)
    {
        const string construction = "throw new InvalidOperationException(\"x\");";
        var firstBody = move ? from == "get" ? "return 0;" : "" : construction;
        var secondBody = move ? construction : "";
        var member = from == "get"
            ? $"int Value {{ {from} {{ {firstBody} }} {to} {{ {secondBody} }} }}"
            : $"event Action Changed {{ {from} {{ {firstBody} }} {to} {{ {secondBody} }} }}";
        var tree = CSharpSyntaxTree.ParseText("using System; class Sample { " + member + " }",
            new CSharpParseOptions(LanguageVersion.Latest));
        return Scan(CorpusModel(tree, OutputKind.DynamicallyLinkedLibrary), "src/example.cs");
    }

    [TestMethod]
    public void MissingFrameworkReference_FailsClosed()
    {
        var tree = CSharpSyntaxTree.ParseText("class Sample { void Run() { throw new System.InvalidOperationException(); } }");
        var compilation = CSharpCompilation.Create("MissingFramework", [tree],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.ThrowsExactly<AssertFailedException>(() => Scan(compilation.GetSemanticModel(tree), "src/example.cs"));
    }

    [TestMethod]
    public void SourceDefinedCoreLibrary_FailsClosed()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System { public class Object {} }");
        var compilation = CSharpCompilation.Create("SourceCore", [tree],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.AreEqual(TypeKind.Class, compilation.GetSpecialType(SpecialType.System_Object).TypeKind);
        Assert.ThrowsExactly<AssertFailedException>(() => Scan(compilation.GetSemanticModel(tree), "src/example.cs"));
    }

    [TestMethod]
    public void SourceDefinedFrameworkName_IsNotTheBclException()
    {
        const string source = "namespace System { class InvalidOperationException : Exception { " +
            "public InvalidOperationException(string message) {} } } " +
            "class Sample { void Run() { throw new System.InvalidOperationException(\"x\"); } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        Assert.AreEqual(0, Scan(CorpusModel(tree, OutputKind.DynamicallyLinkedLibrary), "src/example.cs").Length);
    }

    private static Construction[] AggregateSites(IEnumerable<Site> sites)
        => sites.Distinct().GroupBy(site => (site.Path, site.Member, site.Fingerprint))
            .Select(group => new Construction(group.Key.Path, group.Key.Member, group.Key.Fingerprint, group.Count()))
            .OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Member, StringComparer.Ordinal)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal).ToArray();

    [TestMethod]
    public void ConfigurationUnion_CountsDistinctIdenticalSitesInOppositeBranches()
    {
        const string one = "#if DEBUG\nthrow new InvalidOperationException(\"x\");\n#endif";
        const string two = "#if DEBUG\nthrow new InvalidOperationException(\"x\");\n#else\nthrow new InvalidOperationException(\"x\");\n#endif";
        var baseline = ScanConfigurations(one);
        Assert.AreEqual(1, baseline.Single().Count);
        var growth = ScanConfigurations(two);
        Assert.AreEqual(2, growth.Single().Count);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(growth, baseline));
        Assert.AreEqual(1, ScanConfigurations("throw new InvalidOperationException(\"x\");").Single().Count);
    }

    private static Construction[] ScanConfigurations(string body)
    {
        var sites = new List<Site>();
        foreach (var symbols in new[] { new[] { "DEBUG" }, Array.Empty<string>() })
        {
            var model = SnippetModel(body, "src/example.cs", "Run", symbols);
            sites.AddRange(ScanSites(model, "src/example.cs"));
        }

        return AggregateSites(sites);
    }

    private static string Fingerprint(SyntaxNode node)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            node.DescendantTokens().Select(token => new { token.RawKind, token.Text })))));

    private static void AssertSubset(Construction[] actual, Construction[] baseline)
    {
        var pinned = baseline.ToDictionary(item => (item.Path, item.Member, item.Fingerprint), item => item.Count);
        var additions = actual.Where(item => !pinned.TryGetValue((item.Path, item.Member, item.Fingerprint), out var count)
            || item.Count > count).ToArray();
        Assert.AreEqual(0, additions.Length,
            "New/replaced unmarked InvalidOperationException construction(s): " + JsonSerializer.Serialize(additions));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsProductionSource(string root, string path)
    {
        var relative = Relative(root, path);
        return relative.StartsWith("src/", StringComparison.Ordinal)
            && !relative.Split('/').Any(part => part is "obj" or "bin");
    }
}
