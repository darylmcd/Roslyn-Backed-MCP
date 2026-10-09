using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>Pin existing unmarked constructions by semantic member and token identity, not count.</summary>
[TestClass]
public sealed class InvalidOperationConstructionRatchetTests
{
    private const string _syntheticPath = "src/Synthetic/Example.cs";
    private const string _factoryPath = "src/RoslynMcp.Core/Services/InvalidOperationErrors.cs";
    private const string _factoryMember = "M:RoslynMcp.Core.Services.InvalidOperationErrors.Internal(System.String,System.Exception)";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions _inventoryReadOptions = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    public TestContext TestContext { get; set; } = null!;

    public sealed record Construction(string Path, string Member, string Fingerprint, int Count);
    private sealed record Site(string Path, string Member, string Fingerprint, int Offset);

    [TestMethod]
    [TestCategory("RepoSolution")]
    public async Task ProductionConstructions_DoNotGrowOrReplacePinnedIdentitiesAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var services = TestServiceContainer.Create(new ValidationServiceOptions());
        var solutions = new List<Solution>();
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var status = await services.WorkspaceManager.LoadAsync(
                Path.Combine(root, "RoslynMcp.slnx"),
                new Dictionary<string, string> { ["Configuration"] = configuration },
                CancellationToken.None);
            solutions.Add(services.WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
        }

        await ValidateProductionInventoryAsync(solutions, root,
            Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => IsProductionSource(root, path)).Select(path => Relative(root, path)).ToArray(),
            File.ReadAllText(Path.Combine(root, "tests", "RoslynMcp.Tests", "TestData",
                "invalid-operation-construction-baseline.json")));
    }

    private async Task<Construction[]> ValidateProductionInventoryAsync(IEnumerable<Solution> solutions,
        string root, string[] expectedPaths, string baselineJson)
    {
        var inventory = new List<Site>();
        foreach (var solution in solutions)
        {
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

            CollectionAssert.AreEquivalent(expectedPaths, paths.ToArray(),
                "Every production source file must belong to the semantic inventory.");
            Assert.AreEqual(1, factoryCount, "Only the factory's exact member may construct a new plain exception.");
            // Retain offsets only until configurations are unioned; persisted identities never include positions.
            inventory.AddRange(constructions);
        }

        var actual = AggregateSites(inventory);
        TestContext.WriteLine("INVENTORY-BEGIN" + JsonSerializer.Serialize(actual, _jsonOptions) + "INVENTORY-END");
        var baseline = JsonSerializer.Deserialize<Construction[]>(baselineJson, _inventoryReadOptions);
        Assert.IsNotNull(baseline);
        foreach (var item in baseline)
        {
            Assert.IsNotNull(item, "Inventory records must not be null.");
            Assert.IsTrue(item.Count > 0, "Inventory counts must be positive.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Path));
            Assert.IsTrue(item.Path.StartsWith("src/", StringComparison.Ordinal)
                && item.Path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."))
                && item.Path.EndsWith(".cs", StringComparison.Ordinal) && !item.Path.Any(char.IsControl)
                && !item.Path.Contains('\\'),
                "Inventory paths must be normalized production paths.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Member));
            Assert.IsNotNull(item.Fingerprint);
            Assert.IsTrue(item.Fingerprint.Length == 64 && item.Fingerprint.All(Uri.IsHexDigit),
                "Construction fingerprints must be SHA-256 hex.");
        }

        Assert.AreEqual(baseline.Length,
            baseline.Select(item => (item.Path, item.Member, item.Fingerprint)).Distinct().Count(),
            "Inventory identities must not be duplicated.");
        AssertSubset(actual, baseline);
        return actual;
    }

    [TestMethod]
    public async Task CompleteProductionValidator_AllDebtRemovedAllowsEmptyInventoryAsync()
    {
        await ValidateSyntheticProductionAsync("", "[]");
    }

    [TestMethod]
    public async Task CompleteProductionValidator_LiveDebtRejectsEmptyInventoryAsync()
    {
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateSyntheticProductionAsync("throw new InvalidOperationException(\"live\");", "[]"));
    }

    [TestMethod]
    public void SiblingLocalFunctions_CannotExchangeIdenticalConstructions()
    {
        const string before = "{ void Local() { throw new InvalidOperationException(\"x\"); } } " +
            "{ void Local() { } }";
        const string after = "{ void Local() { } } " +
            "{ void Local() { throw new InvalidOperationException(\"x\"); } }";
        var baseline = ScanSnippet(before);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(ScanSnippet(after), baseline));
    }

    [TestMethod]
    public void LocalScopes_AllowDeclarationTriviaMovementAndConstructionOrFunctionDeletion()
    {
        const string original = "{ void Local() { throw new InvalidOperationException(\"x\"); } int first = 1; } " +
            "{ void Local() { throw new InvalidOperationException(\"y\"); } }";
        var baseline = ScanSnippet(original);
        AssertSubset(ScanSnippet("\n{ int first = 1; /*before*/ void Local() { " +
            "throw new /*inside*/ InvalidOperationException(\"x\"); } }\n" +
            "{ void Local() { throw new InvalidOperationException(\"y\"); } }"), baseline);
        AssertSubset(ScanSnippet("{ void Local() { } int first = 1; } " +
            "{ void Local() { throw new InvalidOperationException(\"y\"); } }"), baseline);
        AssertSubset(ScanSnippet("{ int first = 1; } " +
            "{ void Local() { throw new InvalidOperationException(\"y\"); } }"), baseline);
        AssertSubset([], baseline);
    }

    [TestMethod]
    public void LocalScopes_AllowUniquelyHeadedEarlierScopeDeletion()
    {
        const string before = "if (true) { void Local() { throw new InvalidOperationException(\"x\"); } } " +
            "if (false) { void Local() { throw new InvalidOperationException(\"y\"); } }";
        var baseline = ScanSnippet(before);
        AssertSubset(ScanSnippet("if (false) { void Local() { " +
            "throw new InvalidOperationException(\"y\"); } }"), baseline);
    }

    [TestMethod]
    public void LocalScopes_KeepThenIdentityWhenElseScopeIsDeleted()
    {
        var baseline = ScanSnippet("if (true) { void Local() { " +
            "throw new InvalidOperationException(\"x\"); } } else { void Local() { } }");
        AssertSubset(ScanSnippet("if (true) { void Local() { " +
            "throw new InvalidOperationException(\"x\"); } }"), baseline);
    }

    [TestMethod]
    public void LocalScopes_KeepUnbracedControlAncestryDistinct()
    {
        var baseline = ScanSnippet("if (true) if (true) { void Local() { " +
            "throw new InvalidOperationException(\"x\"); } }");
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("if (false) if (true) { void Local() { " +
                "throw new InvalidOperationException(\"x\"); } }"), baseline));
    }

    [TestMethod]
    [DataRow("while (true)", "while (false)")]
    [DataRow("for (int i = 0; i < 1; i++)", "for (int i = 0; i < 2; i++)")]
    [DataRow("lock (\"first\")", "lock (\"second\")")]
    public void LocalScopes_RetainGenericControlHeaders(string before, string after)
    {
        const string body = " { void Local() { throw new InvalidOperationException(\"x\"); } }";
        var baseline = ScanSnippet(before + body);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(ScanSnippet(after + body), baseline));
    }

    [TestMethod]
    public void LocalScopes_RejectAmbiguousWholeScopeDeletionWithoutReassigningOldIdentity()
    {
        const string before = "{ void Local() { } } " +
            "{ void Local() { throw new InvalidOperationException(\"x\"); } }";
        var baseline = ScanSnippet(before);
        var remaining = ScanSnippet("{ void Local() { throw new InvalidOperationException(\"x\"); } }");
        Assert.AreNotEqual(baseline.Single().Member, remaining.Single().Member);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(remaining, baseline));
        AssertSubset([], baseline);
    }

    [TestMethod]
    [DataRow("null", false)]
    [DataRow("[null]", false)]
    [DataRow("[{}]", false)]
    [DataRow("[{\"Path\":null,\"Member\":\"M:x\",\"Fingerprint\":\"x\",\"Count\":1}]", false)]
    [DataRow("[{\"Path\":\"src/a.cs\",\"Member\":null,\"Fingerprint\":\"x\",\"Count\":1}]", false)]
    [DataRow("[{\"Path\":\"src/a.cs\",\"Member\":\"M:x\",\"Fingerprint\":null,\"Count\":1}]", false)]
    [DataRow("[", true)]
    [DataRow("{}", true)]
    [DataRow("[{\"Path\":\"src/a.cs\",\"Member\":\"M:x\",\"Fingerprint\":\"x\",\"Count\":1,\"Unexpected\":true}]", true)]
    public async Task CompleteProductionValidator_RejectsMalformedInventoryAsync(string json, bool syntaxError)
    {
        if (syntaxError)
        {
            await Assert.ThrowsExactlyAsync<JsonException>(() => ValidateSyntheticProductionAsync("", json));
        }
        else
        {
            await Assert.ThrowsExactlyAsync<AssertFailedException>(() => ValidateSyntheticProductionAsync("", json));
        }
    }

    [TestMethod]
    public async Task CompleteProductionValidator_RejectsInvalidFieldsAndDuplicateRecordsAsync()
    {
        var valid = ScanSnippet("throw new InvalidOperationException(\"x\");").Single();
        var invalid = new[]
        {
            valid with { Path = "../outside.cs" }, valid with { Path = "src/../a.cs" },
            valid with { Path = "src\\a.cs" }, valid with { Path = "src//a.cs" },
            valid with { Path = "src/./a.cs" }, valid with { Path = "src/a/" },
            valid with { Member = " " },
            valid with { Fingerprint = new string('Z', 64) }, valid with { Fingerprint = "" },
            valid with { Count = 0 }, valid with { Count = -1 },
        };
        foreach (var item in invalid)
        {
            await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
                ValidateSyntheticProductionAsync("", JsonSerializer.Serialize(new[] { item })));
        }

        await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateSyntheticProductionAsync("", JsonSerializer.Serialize(new[] { valid, valid })));
    }

    private async Task ValidateSyntheticProductionAsync(string body, string baselineJson)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        await ValidateProductionInventoryAsync(CreateSyntheticProductionSolutions(workspace, root,
            "using System; class Example { void Run() { " + body + " } }"),
            root, [_factoryPath, _syntheticPath], baselineJson);
    }

    private static Solution[] CreateSyntheticProductionSolutions(AdhocWorkspace workspace, string root, string source)
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.IsNotNull(trustedAssemblies);
        var project = workspace.AddProject("Synthetic", LanguageNames.CSharp)
            .Solution.WithProjectFilePath(workspace.CurrentSolution.ProjectIds.Single(),
                Path.Combine(root, "src", "Synthetic", "Synthetic.csproj"))
            .GetProject(workspace.CurrentSolution.ProjectIds.Single())!
            .WithMetadataReferences(trustedAssemblies.Split(Path.PathSeparator)
                .Append(typeof(RoslynMcp.Core.Services.PublicInvalidOperationException).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        const string factory = "using System; namespace RoslynMcp.Core.Services; " +
            "public static class InvalidOperationErrors { " +
            "public static InvalidOperationException Internal(string serverDetail, Exception inner = null) " +
            "=> new InvalidOperationException(serverDetail, inner); }";
        project = project.AddDocument("Factory.cs", SourceText.From(factory),
            filePath: Path.Combine(root, _factoryPath)).Project;
        project = project.AddDocument("Example.cs", SourceText.From(source),
            filePath: Path.Combine(root, _syntheticPath)).Project;
        return new[] { new[] { "DEBUG" }, Array.Empty<string>() }.Select(symbols =>
            project.WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest,
                preprocessorSymbols: symbols)).Solution).ToArray();
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
            var target = model.GetTypeInfo(node).Type;
            Assert.IsNotNull(target, $"Construction target did not resolve: {path}");
            Assert.AreNotEqual(TypeKind.Error, target.TypeKind, $"Construction target did not resolve: {path}");
            if (!SymbolEqualityComparer.Default.Equals(target, exactType))
            {
                continue;
            }

            AssertConstructorTargetIdentity(model, node, exactType, path);

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

    private static void AssertConstructorTargetIdentity(SemanticModel model,
        BaseObjectCreationExpressionSyntax node, ITypeSymbol expectedTarget, string path)
    {
        var operation = model.GetOperation(node);
        IMethodSymbol[] constructors;
        if (operation is IDynamicObjectCreationOperation dynamicCreation)
        {
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(dynamicCreation.Type, expectedTarget),
                "Dynamic construction must retain the exact target.");
            var symbols = model.GetSymbolInfo(node);
            Assert.IsTrue(symbols.Symbol is IMethodSymbol
                || symbols.Symbol is null && symbols.CandidateReason == CandidateReason.LateBound
                && symbols.CandidateSymbols.Length > 0
                && symbols.CandidateSymbols.All(candidate => candidate is IMethodSymbol),
                $"Unresolved construction identity: {path}");
            constructors = symbols.Symbol is IMethodSymbol selected
                ? [selected] : symbols.CandidateSymbols.Cast<IMethodSymbol>().ToArray();
        }
        else
        {
            var constructor = (operation as IObjectCreationOperation)?.Constructor;
            Assert.IsNotNull(constructor, $"Unresolved construction at {path}");
            constructors = [constructor];
        }

        Assert.IsTrue(constructors.All(constructor => constructor.MethodKind == MethodKind.Constructor
            && SymbolEqualityComparer.Default.Equals(constructor.ContainingType, expectedTarget)),
            $"Construction identity must belong to the exact target: {path}");
    }

    private static string MemberIdentity(ISymbol member)
    {
        if (member is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
        {
            // Roslyn can emit a documentation ID for a local function without its lexical owner.
            // Preserve the entire containing-member chain, including nested local functions.
            Assert.IsNotNull(member.ContainingSymbol);
            return MemberIdentity(member.ContainingSymbol) + "/" +
                member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) + LexicalScopeIdentity(member);
        }

        var documentationId = member.GetDocumentationCommentId();
        Assert.IsNotNull(documentationId, $"Member needs a stable identity: {member}");
        return documentationId;
    }

    private sealed record ScopeFrame(string Header, int PeerIndex, int PeerCount);

    private static string LexicalScopeIdentity(ISymbol member)
    {
        var declarations = member.DeclaringSyntaxReferences;
        Assert.AreEqual(1, declarations.Length, "A local function must have one source declaration.");
        var declaration = declarations.Single().GetSyntax();
        var frames = declaration.Ancestors().OfType<BlockSyntax>().Reverse()
            .Where(IsLexicalScope).Select(block =>
            {
                var parentBlock = block.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
                var root = parentBlock is null ? block.SyntaxTree.GetRoot() : parentBlock;
                var header = ScopeHeader(block);
                var peers = root.DescendantNodes().OfType<BlockSyntax>()
                    .Where(peer => IsLexicalScope(peer)
                        && peer.Ancestors().OfType<BlockSyntax>().FirstOrDefault() == parentBlock
                        && ScopeHeader(peer) == header).ToArray();
                var index = Array.IndexOf(peers, block);
                Assert.IsTrue(index >= 0, "Lexical scope must belong to its peer inventory.");
                return new ScopeFrame(header, index, peers.Length);
            }).ToArray();
        return "@scopes:" + JsonSerializer.Serialize(frames) + ConditionalScopeIdentity(declaration);
    }


    private sealed record ConditionalFrame(string Condition, string Branch);

    private static string ConditionalScopeIdentity(SyntaxNode declaration)
    {
        var frames = new List<ConditionalFrame>();
        foreach (var directive in declaration.SyntaxTree.GetRoot().DescendantTrivia(descendIntoTrivia: true)
                     .Select(trivia => trivia.GetStructure()).OfType<DirectiveTriviaSyntax>()
                     .OrderBy(directive => directive.SpanStart)
                     .TakeWhile(directive => directive.SpanStart < declaration.SpanStart))
        {
            Assert.IsFalse(directive.ContainsDiagnostics, "Directive syntax must resolve before assigning an identity.");
            switch (directive)
            {
                case IfDirectiveTriviaSyntax conditional:
                    frames.Add(new ConditionalFrame(Fingerprint(conditional.Condition), "if"));
                    break;
                case ElifDirectiveTriviaSyntax conditional:
                    Assert.IsTrue(frames.Count > 0, "An elif branch must have a containing conditional.");
                    frames[^1] = frames[^1] with { Branch = "elif:" + Fingerprint(conditional.Condition) };
                    break;
                case ElseDirectiveTriviaSyntax:
                    Assert.IsTrue(frames.Count > 0, "An else branch must have a containing conditional.");
                    frames[^1] = frames[^1] with { Branch = "else" };
                    break;
                case EndIfDirectiveTriviaSyntax:
                    Assert.IsTrue(frames.Count > 0, "An endif directive must have a containing conditional.");
                    frames.RemoveAt(frames.Count - 1);
                    break;
            }
        }

        return frames.Count == 0 ? "" : "@conditions:" + JsonSerializer.Serialize(frames);
    }

    private static bool IsLexicalScope(BlockSyntax block)
        => block.Parent is not (BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax);

    private static string ScopeHeader(BlockSyntax block)
    {
        if (block.Parent is BlockSyntax or GlobalStatementSyntax)
        {
            return "bare";
        }

        return string.Join("/", block.Ancestors().TakeWhile(node => node is not BlockSyntax)
            .Reverse().Select(node => node switch
            {
                IfStatementSyntax conditional => "if:" + Fingerprint(conditional.Condition),
                ElseClauseSyntax => "else",
                SwitchStatementSyntax selection => "switch:" + Fingerprint(selection.Expression),
                SwitchSectionSyntax section => "case:" + TokenFingerprint(
                    section.Labels.SelectMany(label => label.DescendantTokens())),
                _ => node.Kind() + ":" + TokenFingerprint(node.DescendantTokens().Where(token =>
                    !token.Parent!.AncestorsAndSelf().TakeWhile(ancestor => ancestor != node)
                        .OfType<StatementSyntax>().Any())),
            }));
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


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConditionalLocalFunctions_CannotExchangeIdenticalConstructionsAsync(bool directBody)
    {
        const string construction = "void Local() { throw new InvalidOperationException(\"x\"); }";
        const string empty = "void Local() { }";
        string Scope(string local) => directBody ? local : "{ " + local + " }";
        var before = "#if DEBUG\n" + Scope(construction) + "\n#else\n" + Scope(empty) + "\n#endif";
        var after = "#if DEBUG\n" + Scope(empty) + "\n#else\n" + Scope(construction) + "\n#endif";
        var baseline = ScanConfigurations(before);
        Assert.AreEqual(1, baseline.Single().Count);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(ScanConfigurations(after), baseline));
        var productionBaseline = JsonSerializer.Serialize(baseline.Select(item =>
            item with { Path = "src/Synthetic/Example.cs", Member = item.Member.Replace("Sample", "Example") }));
        await ValidateSyntheticProductionAsync("\n" + before, productionBaseline);
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateSyntheticProductionAsync("\n" + after, productionBaseline));
    }

    [TestMethod]
    public void ConditionalLocalFunctions_RetainNestedElifBranchAndConditionIdentity()
    {
        const string local = "{ void Local() { throw new InvalidOperationException(\"x\"); } }";
        var baseline = ScanConfigurations("#if DEBUG\n#if true\n" + local + "\n#endif\n#endif");
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanConfigurations("#if DEBUG\n#if false\n#else\n" + local + "\n#endif\n#endif"), baseline));
        var elif = ScanConfigurations("#if DEBUG\n{ void Local() { } }\n#elif !DEBUG\n" + local + "\n#endif");
        Assert.AreEqual(1, elif.Single().Count);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanConfigurations("#if DEBUG\n{ void Local() { } }\n#else\n" + local + "\n#endif"), elif));
    }

    [TestMethod]
    public void ConditionalLocalFunctions_AllowDirectiveTriviaMovementAndDeletion()
    {
        const string original = "#if DEBUG\n{ void Local() { throw new InvalidOperationException(\"x\"); } int first = 1; }\n#else\n{ void Local() { throw new InvalidOperationException(\"y\"); } }\n#endif";
        var baseline = ScanConfigurations(original);
        Assert.AreEqual(2, baseline.Sum(item => item.Count));
        AssertSubset(ScanConfigurations("\n#if   DEBUG // moved\n\n{ int first = 1; /*moved*/ void Local() { throw new /*inside*/ InvalidOperationException(\"x\"); } }\n#else // moved\n{ void Local() { throw new InvalidOperationException(\"y\"); } }\n#endif\n"), baseline);
        AssertSubset(ScanConfigurations("#if DEBUG\n{ int first = 1; }\n#else\n{ void Local() { throw new InvalidOperationException(\"y\"); } }\n#endif"), baseline);
        AssertSubset([], baseline);
    }

    [TestMethod]
    public void SwitchLocalFunctions_RetainCaseAndSelectionHeadersAndAllowUniqueScopeDeletion()
    {
        const string live = "{ void Local() { throw new InvalidOperationException(\"x\"); } } break; ";
        const string empty = "{ void Local() { } } break; ";
        var baseline = ScanSnippet("switch (1) { case 1: " + live + "case 2: " + empty + "}");
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("switch (1) { case 1: " + empty + "case 2: " + live + "}"), baseline));
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(
            ScanSnippet("switch (2) { case 1: " + live + "case 2: " + empty + "}"), baseline));
        var deletionBaseline = ScanSnippet("switch (1) { case 1: " + empty + "case 2: " + live + "}");
        AssertSubset(ScanSnippet("switch (1) { case 2: " + live + "}"), deletionBaseline);
        AssertSubset(ScanSnippet("switch ( 1 ) { case 2: /*moved*/ " + live + "}"), deletionBaseline);
    }

    [TestMethod]
    public void TopLevelLocalFunctions_RetainSiblingScopeIdentityAndFailClosedOnAmbiguousDeletion()
    {
        const string live = "{ void Local() { throw new System.InvalidOperationException(\"x\"); } }";
        const string empty = "{ void Local() { } }";
        Construction[] ScanTop(string source) => Scan(CorpusModel(CSharpSyntaxTree.ParseText(source),
            OutputKind.ConsoleApplication), "src/Program.cs");
        var baseline = ScanTop(empty + live);
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(ScanTop(live + empty), baseline));
        Assert.ThrowsExactly<AssertFailedException>(() => AssertSubset(ScanTop(live), baseline));
        AssertSubset(ScanTop("\n" + empty + "\n/*moved*/" + live), baseline);
        AssertSubset(ScanTop(empty + "{ }"), baseline);
    }

    [TestMethod]
    [DataRow("#else")]
    [DataRow("#elif DEBUG")]
    [DataRow("#endif")]
    public void ConditionalLocalFunctions_RejectUnmatchedDirectives(string directive)
    {
        Assert.ThrowsExactly<AssertFailedException>(() => ScanConfigurations(directive +
            "\nvoid Local() { throw new InvalidOperationException(\"x\"); }"));
    }

    [TestMethod]
    public void ConditionalLocalFunctions_IgnoreNonConditionalDirectivesAndCompletedConditions()
    {
        const string live = "{ void Local() { throw new InvalidOperationException(\"x\"); } }";
        var baseline = ScanConfigurations(live);
        AssertSubset(ScanConfigurations("#region moved\n#if DEBUG\n#endif\n" + live + "\n#endregion"), baseline);
    }


    [TestMethod]
    [DataRow("new OtherType(value)")]
    [DataRow("new OtherType((dynamic)value)")]
    [DataRow("new(value)")]
    [DataRow("new((dynamic)value)")]
    [DataRow("new OtherType(value: value)")]
    [DataRow("new(value: value)")]
    [DataRow("new OtherAlias(value)")]
    public async Task CompleteProductionValidator_AllowsResolvedNonBclDynamicOverloadsAsync(string creation)
    {
        const string declarations = "using OtherAlias = OtherType; public class OtherType { " +
            "public OtherType(string value) {} public OtherType(int value) {} } ";
        await ValidateDiagnosticFreeSyntheticSourceAsync(declarations +
            "public class Example { public OtherAlias Make(dynamic value) => " + creation + "; }", "[]");
    }

    [TestMethod]
    [DataRow("OtherType<int>", "public class OtherType<T> { public OtherType(string value) {} public OtherType(int value) {} }", "new OtherType<int>(value)")]
    [DataRow("OtherType<int>", "public class OtherType<T> { public OtherType(string value) {} public OtherType(int value) {} }", "new(value)")]
    [DataRow("OtherException", "public class OtherException : System.InvalidOperationException { public OtherException(string value) {} public OtherException(int value) {} }", "new OtherException(value)")]
    [DataRow("OtherException", "public class OtherException : System.InvalidOperationException { public OtherException(string value) {} public OtherException(int value) {} }", "new(value)")]
    public async Task CompleteProductionValidator_AllowsResolvedNonBclGenericAndDerivedTargetsAsync(
        string target, string declarations, string creation)
    {
        await ValidateDiagnosticFreeSyntheticSourceAsync(declarations +
            " public class Example { public " + target + " Make(dynamic value) => " + creation + "; }", "[]");
    }

    [TestMethod]
    [DataRow("new PublicInvalidOperationException(value)")]
    [DataRow("new(value)")]
    [DataRow("new PublicInvalidOperationException((dynamic)value)")]
    [DataRow("new((dynamic)value)")]
    public async Task CompleteProductionValidator_AllowsActualPublicMarkerDynamicConstructionAsync(string creation)
    {
        await ValidateDiagnosticFreeSyntheticSourceAsync("using RoslynMcp.Core.Services; " +
            "public class Example { public PublicInvalidOperationException Make(dynamic value) => " + creation + "; }", "[]");
    }

    [TestMethod]
    [DataRow("new System.InvalidOperationException(value)")]
    [DataRow("new(value)")]
    [DataRow("new System.InvalidOperationException((dynamic)value)")]
    [DataRow("new((dynamic)value)")]
    public async Task CompleteProductionValidator_InventoriesExactBclDynamicConstructionAsync(string creation)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        var solutions = CreateSyntheticProductionSolutions(workspace, root,
            "public class Example { public System.InvalidOperationException Make(dynamic value) => " + creation + "; }");
        await AssertDiagnosticFreeAsync(solutions);
        var inventory = AggregateSites((await Task.WhenAll(solutions.Select(async solution =>
        {
            var project = solution.Projects.Single();
            var compilation = await project.GetCompilationAsync();
            Assert.IsNotNull(compilation);
            var tree = compilation.SyntaxTrees.Single(tree => Relative(root, tree.FilePath) == _syntheticPath);
            var model = compilation.GetSemanticModel(tree);
            var node = tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Single();
            var operation = model.GetOperation(node);
            Assert.IsTrue(operation is IDynamicObjectCreationOperation, "BCL corpus must exercise actual dynamic binding.");
            var symbols = model.GetSymbolInfo(node);
            TestContext.WriteLine($"BCL-DYNAMIC operation={operation.Kind} selected={symbols.Symbol} " +
                $"reason={symbols.CandidateReason} candidates={symbols.CandidateSymbols.Length}");
            return ScanSites(model, _syntheticPath);
        }))).SelectMany(sites => sites));
        Assert.AreEqual(1, inventory.Single().Count);
        await ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath],
            JsonSerializer.Serialize(inventory));
        await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath], "[]"));
    }

    [TestMethod]
    [DataRow("MissingType", "new MissingType(value)")]
    [DataRow("MissingType", "new(value)")]
    public async Task CompleteProductionValidator_RefusesUnresolvedDynamicTargetsAsync(string target, string creation)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        var solutions = CreateSyntheticProductionSolutions(workspace, root,
            "public class Example { public " + target + " Make(dynamic value) => " + creation + "; }");
        foreach (var solution in solutions)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            Assert.IsNotNull(compilation);
            Assert.IsTrue(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath], "[]"));
    }


    [TestMethod]
    [DataRow("new System.InvalidOperationException(1, 2, 3)")]
    [DataRow("new(1, 2, 3)")]
    public async Task CompleteProductionValidator_RefusesUnresolvedExactBclConstructorsAsync(string creation)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        var solutions = CreateSyntheticProductionSolutions(workspace, root,
            "public class Example { public System.InvalidOperationException Make() => " + creation + "; }");
        foreach (var solution in solutions)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            Assert.IsNotNull(compilation);
            Assert.IsTrue(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        var exception = await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath], "[]"));
        StringAssert.Contains(exception.Message, "Unresolved construction");
    }

    [TestMethod]
    public void ContainingSymbolWithoutDocumentationIdentity_FailsClosed()
    {
        var assembly = SnippetModel("throw new InvalidOperationException(\"x\");",
            "src/example.cs", "Run", []).Compilation.Assembly;
        Assert.IsNull(assembly.GetDocumentationCommentId());
        Assert.ThrowsExactly<AssertFailedException>(() => MemberIdentity(assembly));
    }


    [TestMethod]
    [DataRow("new T()")]
    [DataRow("new()")]
    public async Task CompleteProductionValidator_AllowsResolvedTypeParameterTargetAsync(string creation)
    {
        await ValidateDiagnosticFreeSyntheticSourceAsync(
            "public class Example { public T Make<T>() where T : new() => " + creation + "; }", "[]");
    }


    [TestMethod]
    [DataRow("new OtherType(value)", true)]
    [DataRow("new OtherType(1)", false)]
    public async Task ConstructorEvidence_RequiresSelectedOrCandidateConstructorsOnTheKnownTargetAsync(
        string creation, bool lateBound)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        var solutions = CreateSyntheticProductionSolutions(workspace, root,
            "public class OtherType { public OtherType(string value) {} public OtherType(int value) {} } " +
            "public class Example { public OtherType Make(dynamic value) => " + creation + "; }");
        await AssertDiagnosticFreeAsync(solutions);
        foreach (var solution in solutions)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            Assert.IsNotNull(compilation);
            var tree = compilation.SyntaxTrees.Single(tree => Relative(root, tree.FilePath) == _syntheticPath);
            var model = compilation.GetSemanticModel(tree);
            var node = tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Single();
            var target = model.GetTypeInfo(node).Type;
            Assert.IsNotNull(target);
            var symbols = model.GetSymbolInfo(node);
            if (lateBound)
            {
                Assert.IsNull(symbols.Symbol);
                Assert.AreEqual(CandidateReason.LateBound, symbols.CandidateReason);
                Assert.AreEqual(2, symbols.CandidateSymbols.Length);
            }
            else
            {
                Assert.IsTrue(symbols.Symbol is IMethodSymbol);
                Assert.AreEqual(CandidateReason.None, symbols.CandidateReason);
            }
            AssertConstructorTargetIdentity(model, node, target, _syntheticPath);
            var differentTarget = compilation.GetSpecialType(SpecialType.System_Object);
            Assert.ThrowsExactly<AssertFailedException>(() =>
                AssertConstructorTargetIdentity(model, node, differentTarget, _syntheticPath));
            TestContext.WriteLine($"CONSTRUCTOR-EVIDENCE selected={symbols.Symbol} " +
                $"reason={symbols.CandidateReason} candidates={symbols.CandidateSymbols.Length}");
        }

        await ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath], "[]");
        await AssertDiagnosticFreeAsync(solutions);
    }


    [TestMethod]
    public async Task CompleteProductionValidator_RefusesMissingTargetTypedIdentityAsync()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        var solutions = CreateSyntheticProductionSolutions(workspace, root,
            "public class Example { public object Make(dynamic value) { var result = new(value); return result; } }");
        foreach (var solution in solutions)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            Assert.IsNotNull(compilation);
            var tree = compilation.SyntaxTrees.Single(tree => Relative(root, tree.FilePath) == _syntheticPath);
            var node = tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Single();
            var target = compilation.GetSemanticModel(tree).GetTypeInfo(node).Type;
            Assert.IsNotNull(target);
            Assert.AreEqual(TypeKind.Error, target.TypeKind);
            Assert.IsTrue(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        var exception = await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath], "[]"));
        StringAssert.Contains(exception.Message, "Construction target did not resolve");
    }

    private async Task ValidateDiagnosticFreeSyntheticSourceAsync(string source, string baselineJson)
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var workspace = new AdhocWorkspace();
        var solutions = CreateSyntheticProductionSolutions(workspace, root, source);
        await AssertDiagnosticFreeAsync(solutions);
        await ValidateProductionInventoryAsync(solutions, root, [_factoryPath, _syntheticPath], baselineJson);
        await AssertDiagnosticFreeAsync(solutions);
    }

    private static async Task AssertDiagnosticFreeAsync(IEnumerable<Solution> solutions)
    {
        foreach (var solution in solutions)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            Assert.IsNotNull(compilation);
            foreach (var tree in compilation.SyntaxTrees)
            {
                _ = compilation.GetSemanticModel(tree).GetDiagnostics();
            }

            var diagnostics = compilation.GetDiagnostics();
            Assert.AreEqual(0, diagnostics.Length,
                "Dynamic corpus must have zero compiler diagnostics: " + string.Join("; ", diagnostics));
        }
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
        => TokenFingerprint(node.DescendantTokens());

    private static string TokenFingerprint(IEnumerable<SyntaxToken> tokens)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            tokens.Select(token => new { token.RawKind, token.Text })))));

    private static void AssertSubset(Construction[] actual, Construction[] baseline)
    {
        var pinned = baseline.ToDictionary(item => (item.Path, item.Member, item.Fingerprint), item => item.Count);
        var additions = actual.Where(item => !pinned.TryGetValue((item.Path, item.Member, item.Fingerprint), out var count)
            || item.Count > count).ToArray();
        Assert.AreEqual(0, additions.Length,
            "New/replaced unmarked InvalidOperationException construction(s), or unproven lexical scope topology. " +
            "Use PublicInvalidOperationException for safe caller refusals or InvalidOperationErrors.Internal " +
            "for server-only diagnostics; do not reassign an old identity: " + JsonSerializer.Serialize(additions));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsProductionSource(string root, string path)
    {
        var relative = Relative(root, path);
        return relative.StartsWith("src/", StringComparison.Ordinal)
            && !relative.Split('/').Any(part => part is "obj" or "bin");
    }
}
