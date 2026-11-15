using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExtractionNamespaceIdentityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtractSharedExpression_CrossTypeCallBindsToFullNestedIdentity(bool callerSharesDocument)
    {
        const string ownerSource = """
            namespace @event.Outer
            {
                namespace Inner
                {
                    public class Container
                    {
                        public class Owner
                        {
                            public int Read(int input) => input * 31;
                        }
                    }
                }
            }
            """;
        const string callerSource = """
            namespace Inner
            {
                public class Owner
                {
                    public static int SharedValue(int input) => -1;
                }
            }

            namespace Caller
            {
                public class Consumer
                {
                    public int Read(int input) => input * 31;
                }
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "internal", "event.Outer.Inner.Container+Owner", callerSharesDocument);
    }

    [TestMethod]
    public async Task ExtractSharedExpression_OtherPartOfGenericOwnerCanCallPrivateHelper()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public partial class Owner<T>
            {
                public int Read(int input) => input * 31;
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public partial class Owner<T>
            {
                public int ReadAgain(int input) => input * 31;
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "private", "Outer.Inner.Owner`1");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_OtherPartialDeclarationDoesNotCallLocalDelegate()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public partial class Owner
            {
                public int Read(int input) => input * 31;
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public partial class Owner
            {
                public int ReadAgain(int input)
                {
                    System.Func<int, int> SharedValue = value => -1;
                    _ = SharedValue;
                    return input * 31;
                }
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "private", "Outer.Inner.Owner");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_NestedGenericOwnerKeepsItsEnclosingTypeArgument()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public partial class Container<T>
            {
                public class Owner
                {
                    public int Read(int input) => input * 31;
                }
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public partial class Container<T>
            {
                public class Caller
                {
                    public int ReadAgain(int input) => input * 31;
                }
            }
            """;

        await AssertSharedExpressionPreviewAndApplyAsync(
            ownerSource, callerSource, "internal", "Outer.Inner.Container`1+Owner");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsOwnerTypeParameterFromUnrelatedScope()
    {
        const string ownerSource = """
            namespace Outer.Inner;
            public class Owner<T>
            {
                public int Read(int input) => input * 31;
            }
            """;
        const string callerSource = """
            namespace Outer.Inner;
            public class Caller<T>
            {
                public int Read(int input) => input * 31;
            }
            """;

        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "NamespaceExpressionOwner.cs");
        await File.WriteAllTextAsync(ownerPath, ownerSource);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "NamespaceExpressionCaller.cs"), callerSource);
        var workspaceId = await workspace.LoadAsync();
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            PreviewSharedExpressionAsync(workspaceId, ownerPath, ownerSource, "internal"));
        StringAssert.Contains(error.PublicMessage, "declaring type");
        FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
    }

    private static async Task AssertSharedExpressionPreviewAndApplyAsync(
        string ownerSource, string callerSource, string accessibility, string ownerMetadataName,
        bool callerSharesDocument = false)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "NamespaceExpressionOwner.cs");
        var callerPath = ownerPath;
        if (callerSharesDocument)
        {
            ownerSource = callerSource + Environment.NewLine + ownerSource;
        }
        else
        {
            callerPath = workspace.GetPath("SampleLib", "NamespaceExpressionCaller.cs");
            await File.WriteAllTextAsync(callerPath, callerSource);
        }

        await File.WriteAllTextAsync(ownerPath, ownerSource);
        var workspaceId = await workspace.LoadAsync();
        var json = await PreviewSharedExpressionAsync(workspaceId, ownerPath, ownerSource, accessibility);
        using var preview = JsonDocument.Parse(json);
        var token = preview.RootElement.GetProperty("previewToken").GetString()!;
        Assert.AreEqual(callerSharesDocument ? 1 : 2, preview.RootElement.GetProperty("changes").GetArrayLength(), json);
        await AssertCallsBindAsync(GetPreviewSolution(token), callerPath, ownerMetadataName, callerSharesDocument ? 2 : 1);

        var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
        using var applied = JsonDocument.Parse(appliedJson);
        Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
        await AssertCallsBindAsync(
            WorkspaceManager.GetCurrentSolution(workspaceId), callerPath, ownerMetadataName, callerSharesDocument ? 2 : 1);
    }

    private static Task<string> PreviewSharedExpressionAsync(
        string workspaceId, string ownerPath, string ownerSource, string accessibility)
    {
        var span = CSharpSyntaxTree.ParseText(ownerSource).GetRoot().DescendantNodes()
            .OfType<BinaryExpressionSyntax>().Last().GetLocation().GetLineSpan();
        return ExtractMethodTools.PreviewExtractSharedExpressionToHelper(
            WorkspaceExecutionGate, ExtractMethodService, workspaceId, ownerPath,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
            "SharedValue", accessibility, allowCrossFile: true);
    }

    private static async Task AssertCallsBindAsync(Solution solution, string path, string ownerMetadataName, int expectedCount)
    {
        var compilation = await GetCleanCompilationAsync(solution);
        var expected = compilation.GetTypeByMetadataName(ownerMetadataName);
        Assert.IsNotNull(expected);
        var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        var calls = (await tree.GetRootAsync()).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.HasCount(expectedCount, calls);
        var model = compilation.GetSemanticModel(tree);
        foreach (var call in calls)
        {
            var target = model.GetSymbolInfo(call).Symbol as IMethodSymbol;
            Assert.IsNotNull(target, call.ToString());
            Assert.AreEqual("SharedValue", target.Name);
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(expected, target.ContainingType), target.ToDisplayString());
        }
    }

    [TestMethod]
    public async Task ExtractType_PreservesNestedNamespaceImportScopes()
    {
        const string source = """
            extern alias GlobalInput;
            global using ProjectEntry = GlobalInput::FixtureImports.Entry;
            using FileEntry = GlobalInput::FixtureImports.Entry;

            namespace @event.Outer
            {
                using ParentEntry = global::System.Uri;
                namespace Inner.Deeper
                {
                    extern alias LocalInput;
            #if NEVER_DEFINED
                    using ChildEntry = int;
            #else
                    using ChildEntry = LocalInput::FixtureImports.Entry;
            #endif
                    public class Original
                    {
                        private static ProjectEntry Build(ParentEntry parent, ChildEntry child, FileEntry file) => new();
                        public static object Read() => Build(default!, new(), new());
                    }
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "event.Outer.Inner.Deeper.Extracted", ["Build"],
            "event.Outer.Inner.Deeper.Original", expectComposition: false,
            prepareWorkspace: AddAliasedProjectReferenceAsync);
    }

    private static async Task AddAliasedProjectReferenceAsync(IsolatedWorkspaceScope workspace)
    {
        const string dependency = "FixtureImports";
        AddProjectToCopiedSolution(workspace.RootPath, dependency, "net10.0");
        await File.WriteAllTextAsync(workspace.GetPath(dependency, "Entry.cs"), "namespace FixtureImports; public class Entry { }");
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", $"../{dependency}/{dependency}.csproj"),
            new XElement("Aliases", "GlobalInput,LocalInput"))));
        project.Save(projectPath);
        await RestoreWorkspaceAsync(workspace);
    }

    [TestMethod]
    public async Task ExtractType_StaticAndCompositionReferencesBypassNestedDecoy()
    {
        const string source = """
            namespace Outer
            {
                namespace Inner
                {
                    public class Original
                    {
                        public class Extracted
                        {
                            public static int Build() => -1;
                            public int ReadValue() => -2;
                        }

                        public Original(int _extracted)
                        {
                            _ = _extracted;
                        }

                        private static int Build() => 17;
                        private int ReadValue() => 35;
                        public int Read()
                        {
                            var _extracted = new Original.Extracted();
                            _ = _extracted;
                            return Build() + Original.Build() + ReadValue();
                        }
                    }
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Inner.Extracted", ["Build", "ReadValue"],
            "Outer.Inner.Original", expectComposition: true);
    }

    [TestMethod]
    public async Task ExtractType_FileLocalSymbolsPreserveConditionalNamespaceIdentity()
    {
        const string source = """
            #if FIRST_BRANCH
            #undef USE_SECOND
            #else
            #define USE_SECOND
            #endif

            namespace Outer.
            #if USE_SECOND
                Second
            #else
                First
            #endif
                .Tail
            {
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            }

            namespace Outer.
            #if USE_SECOND
                First
            #else
                Second
            #endif
                .Tail
            {
                public class Extracted
                {
                    public static int Build() => -1;
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Second.Tail.Extracted", ["Build"],
            "Outer.Second.Tail.Original", expectComposition: false, alternateNamespace: "Outer.First.Tail");
    }

    [TestMethod]
    [DataRow("Extracted")]
    [DataRow("__ExtractedType")]
    public async Task ExtractType_ConditionalHeadersWithRepeatedNamespaceKeywordRetainBothBranches(string newTypeName)
    {
        const string source = """
            #if FIRST_BRANCH
            namespace Outer.First
            #else
            namespace Outer.Second
            #endif
            {
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Second." + newTypeName, ["Build"],
            "Outer.Second.Original", expectComposition: false, alternateNamespace: "Outer.First");
    }

    [TestMethod]
    [DataRow("{", "}", false)]
    [DataRow(";", "", false)]
    [DataRow("{", "}", true)]
    [DataRow(";", "", true)]
    public async Task ExtractType_ConditionalHeadersCanOwnTheNamespaceDelimiter(string delimiter, string close, bool hasImport)
    {
        var source = $$"""
            #if FIRST_BRANCH
            namespace Outer.First{{delimiter}}
            #else
            namespace Outer.Second{{delimiter}}
            #endif
            {{(hasImport ? "using ItemCount = global::System.Int32;" : "")}}

            public class Original
            {
                private static {{(hasImport ? "ItemCount" : "int")}} Build() => 17;
                public static int Read() => Build();
            }
            {{close}}
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Second.Extracted", ["Build"],
            "Outer.Second.Original", expectComposition: false, alternateNamespace: "Outer.First");
    }

    [TestMethod]
    public async Task ExtractType_AliasFollowsTheLastConditionalUsing()
    {
        const string source = """
            namespace Outer.
            #if FIRST_BRANCH
                First
            #else
                Second
            #endif
            {
            #if !FIRST_BRANCH
                using ItemCount = global::System.Int32;
            #endif
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Second.Extracted", ["Build"],
            "Outer.Second.Original", expectComposition: false, alternateNamespace: "Outer.First");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtractType_HandlesOptionalNamespaceSegmentInBothLoadedConfigurations(bool firstBranch)
    {
        const string source = """
            namespace Outer
            #if FIRST_BRANCH
                .First
            #endif
            {
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            }
            """;

        var active = firstBranch ? "Outer.First" : "Outer";
        var alternate = firstBranch ? "Outer" : "Outer.First";
        await AssertTypePreviewAndApplyAsync(source, active + ".Extracted", ["Build"], active + ".Original",
            expectComposition: false, alternateNamespace: alternate, prepareWorkspace: firstBranch ? EnableFirstBranch : null);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtractType_ConditionalNamespaceCanSwitchBetweenBlockAndFileScope(bool firstBranch)
    {
        const string source = """
            #if FIRST_BRANCH
            namespace Outer.First;
            #else
            namespace Outer.Second
            {
            #endif
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            #if !FIRST_BRANCH
            }
            #endif
            """;

        var active = firstBranch ? "Outer.First" : "Outer.Second";
        var alternate = firstBranch ? "Outer.Second" : "Outer.First";
        await AssertTypePreviewAndApplyAsync(source, active + ".Extracted", ["Build"], active + ".Original",
            expectComposition: false, alternateNamespace: alternate, prepareWorkspace: firstBranch ? EnableFirstBranch : null);
    }

    private static Task EnableFirstBranch(IsolatedWorkspaceScope workspace)
    {
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("PropertyGroup", new XElement("DefineConstants", "$(DefineConstants);FIRST_BRANCH")));
        project.Save(projectPath);
        return Task.CompletedTask;
    }

    [TestMethod]
    public async Task ExtractType_HeaderCanMutateItsConditionWhileSelectingNamespace()
    {
        const string source = """
            #if FIRST_BRANCH
            #undef FIRST_BRANCH
            namespace Outer.First
            #else
            #define FIRST_BRANCH
            namespace Outer.Second
            #endif
            {
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read()
                    {
            #if USE_SECOND
                        System.Func<int> __ExtractedType = () => -1;
                        _ = __ExtractedType;
            #endif
                        return Build();
                    }

                    public class Extracted
                    {
                        public static int Build() => -2;
                    }
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Second.Extracted", ["Build"],
            "Outer.Second.Original", expectComposition: false, alternateNamespace: "Outer.First");
    }

    [TestMethod]
    public async Task ExtractType_AliasAvoidsAnAlternateNamespaceMemberInAnotherFile()
    {
        const string source = """
            namespace Outer.
            #if FIRST_BRANCH
                First
            #else
                Second
            #endif
            {
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            }
            """;

        await AssertTypePreviewAndApplyAsync(source, "Outer.Second.Extracted", ["Build"], "Outer.Second.Original",
            expectComposition: false, alternateNamespace: "Outer.First",
            prepareWorkspace: workspace => File.WriteAllTextAsync(workspace.GetPath("SampleLib", "AliasDecoy.cs"), """
                #if FIRST_BRANCH
                namespace Outer.First;
                public class __ExtractedType
                {
                    public static int Build() => -1;
                }
                #endif
                """));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ExtractType_ConditionalQualifiedNameRetainsBothNamespaceBranches(bool directiveStartsName, bool directiveEndsName)
    {
        var source = $$"""
            namespace{{(directiveStartsName ? "" : " Outer.")}}
            #if FIRST_BRANCH
                First
            #else
                Second
            #endif
            {{(directiveEndsName ? "" : "    .Tail")}}
            {
                public class Original
                {
                    private static int Build() => 17;
                    public static int Read() => Build();
                }
            }
            """;

        var prefix = directiveStartsName ? "" : "Outer.";
        var suffix = directiveEndsName ? "" : ".Tail";
        await AssertTypePreviewAndApplyAsync(source, prefix + "Second" + suffix + ".Extracted", ["Build"],
            prefix + "Second" + suffix + ".Original", expectComposition: false, alternateNamespace: prefix + "First" + suffix);
    }

    private static async Task AssertTypePreviewAndApplyAsync(
        string source, string extractedMetadataName, string[] members, string originalMetadataName,
        bool expectComposition, string? alternateNamespace = null,
        Func<IsolatedWorkspaceScope, Task>? prepareWorkspace = null)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        if (prepareWorkspace is not null)
            await prepareWorkspace(workspace);

        var sourcePath = workspace.GetPath("SampleLib", "NamespaceExtractionSource.cs");
        var extractedPath = workspace.GetPath("SampleLib", "NamespaceExtractedType.cs");
        await File.WriteAllTextAsync(sourcePath, source);
        var workspaceId = await workspace.LoadAsync();
        await GetCleanCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));

        var server = await GetPathAuthorizedServerAsync();
        var json = await TypeExtractionTools.PreviewExtractType(
            server, WorkspaceExecutionGate, TypeExtractionService,
            workspaceId, sourcePath, "Original", members, GetSimpleName(extractedMetadataName), extractedPath);
        using var preview = JsonDocument.Parse(json);
        var token = preview.RootElement.GetProperty("previewToken").GetString()!;
        Assert.AreEqual(2, preview.RootElement.GetProperty("changes").GetArrayLength(), json);
        Assert.IsFalse(File.Exists(extractedPath));
        await AssertExtractedTypeBindingsAsync(GetPreviewSolution(token), extractedPath, sourcePath, extractedMetadataName,
            originalMetadataName, expectComposition, alternateNamespace);

        var appliedJson = await TypeExtractionTools.ApplyExtractType(
            WorkspaceExecutionGate, RefactoringService, PreviewStore, token);
        using var applied = JsonDocument.Parse(appliedJson);
        Assert.IsTrue(applied.RootElement.GetProperty("success").GetBoolean(), appliedJson);
        Assert.IsTrue(File.Exists(extractedPath));
        await AssertExtractedTypeBindingsAsync(WorkspaceManager.GetCurrentSolution(workspaceId), extractedPath, sourcePath,
            extractedMetadataName, originalMetadataName, expectComposition, alternateNamespace);
    }

    private static async Task AssertExtractedTypeBindingsAsync(
        Solution solution, string extractedPath, string sourcePath, string extractedMetadataName,
        string originalMetadataName, bool expectComposition, string? alternateNamespace)
    {
        var compilation = await GetCleanCompilationAsync(solution);
        await AssertExtractedTypeBindingsAsync(compilation, extractedPath, sourcePath, extractedMetadataName,
            originalMetadataName, expectComposition);
        if (alternateNamespace is not null)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var options = (CSharpParseOptions)tree.Options;
                var symbols = options.PreprocessorSymbolNames.Where(symbol => symbol != "FIRST_BRANCH").Append("USE_SECOND");
                if (!options.PreprocessorSymbolNames.Contains("FIRST_BRANCH"))
                    symbols = symbols.Append("FIRST_BRANCH");
                var alternate = CSharpSyntaxTree.ParseText(await tree.GetTextAsync(), options.WithPreprocessorSymbols(symbols), tree.FilePath);
                compilation = compilation.ReplaceSyntaxTree(tree, alternate);
            }

            AssertNoCompilerErrors(compilation);
            await AssertExtractedTypeBindingsAsync(compilation, extractedPath, sourcePath,
                alternateNamespace + "." + GetSimpleName(extractedMetadataName),
                alternateNamespace + "." + GetSimpleName(originalMetadataName), expectComposition);
        }
    }

    private static string GetSimpleName(string metadataName) => metadataName[(metadataName.LastIndexOf('.') + 1)..];

    private static async Task AssertExtractedTypeBindingsAsync(
        Compilation compilation, string extractedPath, string sourcePath, string extractedMetadataName,
        string originalMetadataName, bool expectComposition)
    {
        var extracted = compilation.GetTypeByMetadataName(extractedMetadataName);
        Assert.IsNotNull(extracted, extractedMetadataName);
        Assert.AreEqual(extractedPath, extracted.Locations.Single().SourceTree!.FilePath);
        var original = compilation.GetTypeByMetadataName(originalMetadataName);
        Assert.IsNotNull(original);
        var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == sourcePath);
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        if (expectComposition)
        {
            var field = original.GetMembers("_extracted").OfType<IFieldSymbol>().Single();
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(extracted, field.Type), field.Type.ToDisplayString());
            var parameter = original.InstanceConstructors.Single().Parameters.Single(parameter => parameter.Name == "extracted");
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(extracted, parameter.Type), parameter.Type.ToDisplayString());
            var assignment = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single()
                .DescendantNodes().OfType<AssignmentExpressionSyntax>().First();
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(field, model.GetSymbolInfo(assignment.Left).Symbol),
                assignment.ToString());
        }

        var read = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Read");
        var calls = read.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.HasCount(expectComposition ? 3 : 1, calls);
        foreach (var call in calls)
        {
            var target = model.GetSymbolInfo(call).Symbol as IMethodSymbol;
            Assert.IsNotNull(target, call.ToString());
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(extracted, target.ContainingType), target.ToDisplayString());
        }
    }

    private static Solution GetPreviewSolution(string token)
    {
        var pending = PreviewStore.Retrieve(token);
        Assert.IsNotNull(pending);
        return pending.Value.ModifiedSolution;
    }

    private static async Task<Compilation> GetCleanCompilationAsync(Solution solution)
    {
        var compilation = await solution.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        AssertNoCompilerErrors(compilation);
        return compilation;
    }

    private static void AssertNoCompilerErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.IsFalse(errors.Any(), string.Join(Environment.NewLine, errors));
    }
}
