using System.Buffers.Binary;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExtractSharedExpressionBindingTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsDifferentSameSpelledCallees()
    {
        const string owner = """
            public class BindingOwner
            {
                private static int GetValue(int offset) => offset + 42;
                public static int Read(int offset) => GetValue(offset);
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                private static int GetValue(int offset) => offset - 42;
                public static int Read(int offset) => GetValue(offset);
            }
            """;

        await AssertBindingRefusalAsync(owner, caller, "GetValue(offset)");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsDifferentBindingsWithAnUnusedLambdaParameter()
    {
        const string owner = """
            public class BindingOwner
            {
                private static int Invoke(System.Func<int, int> compute, int offset) => compute(offset) + offset;
                public static int Read(int offset) => Invoke(v => 42, offset);
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                private static int Invoke(System.Func<long, long> compute, int offset) => (int)compute(offset) - offset;
                public static int Read(int offset) => Invoke(v => 42, offset);
            }
            """;

        await AssertBindingRefusalAsync(owner, caller, "Invoke(v => 42, offset)");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsDifferentContextualLambdasWithTheSameBody()
    {
        const string owner = """
            using System;

            public class BindingOwner
            {
                public static Func<int, int> Read()
                {
                    return v => 42;
                }
            }
            """;
        const string caller = """
            using System;

            public class BindingCaller
            {
                public static Func<long, long> Read()
                {
                    return v => 42;
                }
            }
            """;
        await AssertBindingRefusalAsync(owner, caller, "v => 42");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsSameArithmeticInDifferentOverflowContexts()
    {
        const string owner = """
            public class BindingOwner
            {
                public static int Read(int offset)
                {
                    unchecked { return offset + 42; }
                }
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset)
                {
                    checked { return offset + 42; }
                }
            }
            """;

        await AssertBindingRefusalAsync(owner, caller, "offset + 42");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsDifferentCompilerSuppliedArgumentsToTheSameMethod()
    {
        const string owner = """
            public class BindingOwner
            {
                public static int ReadFirst(int offset) => BindingTarget.GetValue(offset);
            }

            internal static class BindingTarget
            {
                internal static int GetValue(int value,
                    [System.Runtime.CompilerServices.CallerMemberName] string name = "") => value + name.Length;
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int ReadSecond(int offset) => BindingTarget.GetValue(offset);
            }
            """;

        await AssertBindingRefusalAsync(owner, caller, "BindingTarget.GetValue(offset)");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtractSharedExpression_ComparesInheritedConditionalCallsInTheirOriginalFiles(bool candidateEmits)
    {
        const string owner = """
            #define SHARED_EXPRESSION_FEATURE
            public class BindingOwner
            {
                public static int Read(int offset) => BindingTarget.GetValue(
                    () => { BindingTarget.Counter.Tick(); return 41; }, offset);
            }

            public class CounterBase
            {
                [System.Diagnostics.Conditional("SHARED_EXPRESSION_FEATURE")]
                public virtual void Tick() { }
            }

            public sealed class CounterImpl : CounterBase
            {
                public int Count;
                public override void Tick() => Count++;
            }

            internal static class BindingTarget
            {
                internal static CounterImpl Counter { get; } = new();

                internal static int GetValue(System.Func<int> compute, int offset)
                {
                    Counter.Count = 0;
                    return compute() + Counter.Count + offset;
                }
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset) => BindingTarget.GetValue(
                    () => { BindingTarget.Counter.Tick(); return 41; }, offset);
            }
            """;
        const string expression = """
            BindingTarget.GetValue(
                    () => { BindingTarget.Counter.Tick(); return 41; }, offset)
            """;
        if (candidateEmits)
            await AssertQualifiedPreviewAndApplyAsync(owner, "#define SHARED_EXPRESSION_FEATURE\n" + caller, expression);
        else
            await AssertBindingRefusalAsync(owner, caller, expression);
    }

    [TestMethod]
    [DataRow(false, ConditionalDirectiveState.Different)]
    [DataRow(true, ConditionalDirectiveState.Different)]
    [DataRow(true, ConditionalDirectiveState.Equivalent)]
    [DataRow(false, ConditionalDirectiveState.Malformed)]
    public void SharedExpressionBindings_ComparesEffectiveSymbolsForAliasedConditionalCalls(
        bool genericAttribute, ConditionalDirectiveState candidateState)
    {
        var typeParameter = genericAttribute ? "<T>" : string.Empty;
        var typeArgument = genericAttribute ? "<int>" : string.Empty;
        var aliasSource = $$"""
            namespace System.Diagnostics
            {
                public sealed class ConditionalAttribute{{typeParameter}} : System.Attribute
                {
                    public ConditionalAttribute(string condition) { }
                }
            }
            """;
        var exampleSource = $$"""
            #define ONLY_OWNER
            extern alias twin;

            public static class BindingOwner
            {
                public static int Read(int _) => Fixture.Run(() => { Fixture.Tick(); return 41; });
            }

            public static class Fixture
            {
                private static int _count;

                public static int Run(System.Func<int> compute)
                {
                    _count = 0;
                    return compute() + _count;
                }

                [System.Diagnostics.Conditional("COMMON")]
                [twin::System.Diagnostics.Conditional{{typeArgument}}("ONLY_OWNER")]
                public static void Tick() => _count++;
            }
            """;
        const string candidateSource = """
            public static class BindingCaller
            {
                public static int Read(int _) => Fixture.Run(() => { Fixture.Tick(); return 41; });
            }
            """;
        var corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var alias = CSharpCompilation.Create(
            "ConditionalAlias", [CSharpSyntaxTree.ParseText(aliasSource)], [corlib], options);
        var exampleTree = CSharpSyntaxTree.ParseText(exampleSource);
        var candidateInput = candidateState switch
        {
            ConditionalDirectiveState.Different => "#undef ONLY_OWNER\n" + candidateSource,
            ConditionalDirectiveState.Equivalent => "#define TEMPORARY\n#undef TEMPORARY\n" + candidateSource,
            ConditionalDirectiveState.Malformed => candidateSource + "\n#if false\n#define IGNORED",
            _ => throw new ArgumentOutOfRangeException(nameof(candidateState)),
        };
        var candidateTree = CSharpSyntaxTree.ParseText(
            candidateInput, CSharpParseOptions.Default.WithPreprocessorSymbols("ONLY_OWNER"));
        var compilation = CSharpCompilation.Create(
            "AliasedConditions", [exampleTree, candidateTree], [corlib, alias.ToMetadataReference(aliases: ["twin"])], options);
        var shouldMatch = candidateState == ConditionalDirectiveState.Equivalent;
        if (candidateState == ConditionalDirectiveState.Malformed)
        {
            Assert.IsTrue(candidateTree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
        else
        {
            AssertNoCompilerErrors(compilation);
            AssertEmittedResults(compilation, ("BindingOwner", 42), ("BindingCaller", shouldMatch ? 42 : 41));
        }

        var example = GetBinding(exampleTree);
        var candidate = GetBinding(candidateTree);
        Assert.AreEqual(shouldMatch, example.Matches(candidate, CancellationToken.None));

        SharedExpressionBindings GetBinding(SyntaxTree tree)
        {
            var expression = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression.ToString() == "Fixture.Run");
            return new SharedExpressionBindings(
                expression, new SharedExpressionBindings.BindingContext(compilation.GetSemanticModel(tree)),
                [], CancellationToken.None);
        }
    }

    public enum ConditionalDirectiveState
    {
        Different,
        Equivalent,
        Malformed,
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsCollectionElementsWithHiddenAddBindings()
    {
        const string owner = """
            using SourceExtension;

            public class BindingOwner
            {
                public static BindingBag Read(int offset) => [offset];
            }

            public class BindingBag : System.Collections.Generic.IEnumerable<int>
            {
                private readonly System.Collections.Generic.List<int> _values = new();
                public void Store(int value) => _values.Add(value);
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => _values.GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            namespace SourceExtension
            {
                public static class BagOperations
                {
                    public static void Add(this BindingBag bag, int value) => bag.Store(value + 42);
                }
            }
            """;
        const string caller = """
            using OtherExtension;

            public class BindingCaller
            {
                public static BindingBag Read(int offset) => [offset];
            }

            namespace OtherExtension
            {
                public static class BagOperations
                {
                    public static void Add(this BindingBag bag, int value) => bag.Store(value - 42);
                }
            }
            """;
        await AssertBindingRefusalAsync(owner, caller, "[offset]");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_RejectsIdenticalCallsWithDifferentInterceptors()
    {
        const string expression = "BindingTarget.GetValue(offset)";
        const string owner = """
            public class BindingOwner
            {
                public static int Read(int offset) => BindingTarget.GetValue(offset);
            }

            public static class BindingTarget
            {
                public static int GetValue(int value) => value + 42;
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset) => BindingTarget.GetValue(offset);
            }
            """;
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        Assert.IsNotNull(project.Root);
        project.Root.Add(new XElement("PropertyGroup", new XElement("InterceptorsNamespaces", "SampleLib.Generated")));
        project.Save(projectPath);

        var ownerPath = workspace.GetPath("SampleLib", "BindingOwner.cs");
        const string callerName = "BindingCaller.cs";
        var callerPath = workspace.GetPath("SampleLib", callerName);
        await File.WriteAllTextAsync(ownerPath, owner);
        await File.WriteAllTextAsync(callerPath, caller);

        var call = CSharpSyntaxTree.ParseText(caller).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var name = ((MemberAccessExpressionSyntax)call.Expression).Name;
        var checksum = SourceText.From(caller).GetContentHash();
        var fileName = Encoding.UTF8.GetBytes(callerName);
        // Version 1 interceptor locations encode a content hash, identifier position, and display filename.
        var data = new byte[checksum.Length + sizeof(int) + fileName.Length];
        checksum.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(checksum.Length), name.SpanStart);
        fileName.CopyTo(data, checksum.Length + sizeof(int));
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "BindingInterceptor.cs"), $$"""
            namespace SampleLib.Generated
            {
                public static class Interceptors
                {
                    [System.Runtime.CompilerServices.InterceptsLocation(1, "{{Convert.ToBase64String(data)}}")]
                    public static int Replacement(int value) => value - 42;
                }
            }

            namespace System.Runtime.CompilerServices
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                file sealed class InterceptsLocationAttribute(int version, string data) : Attribute
                {
                    public int Version { get; } = version;
                    public string Data { get; } = data;
                }
            }
            """);

        var workspaceId = await workspace.LoadAsync();
        var compilation = await GetCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        AssertNoCompilerErrors(compilation);
        var callerTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == callerPath);
        var intercepted = (await callerTree.GetRootAsync()).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.AreEqual("Replacement", compilation.GetSemanticModel(callerTree).GetInterceptorMethod(intercepted)?.Name);
        await AssertNoBindingPreviewAsync(workspaceId, ownerPath, owner, expression);
    }

    [TestMethod]
    public async Task ExtractSharedExpression_EquivalentQualifiedBindingsPreviewAndApplyAcrossDocuments()
    {
        const string owner = """
            public class BindingOwner
            {
                public static int Read(int offset) => BindingTarget.GetValue(offset);
            }

            internal static class BindingTarget
            {
                internal static int GetValue(int value) => value + 42;
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset) => BindingTarget.GetValue(offset);
            }
            """;
        await AssertQualifiedPreviewAndApplyAsync(owner, caller, "BindingTarget.GetValue(offset)");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_EquivalentLambdaBindingsPreviewAndApplyAcrossDocuments()
    {
        const string owner = """
            public class BindingOwner
            {
                public static int Read(int offset) => BindingTarget.GetValue(v => 42, offset);
            }

            internal static class BindingTarget
            {
                internal static int GetValue(System.Func<int, int> compute, int value) => compute(value) + value;
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset) => BindingTarget.GetValue(v /* parameter */ => 42, offset);
            }
            """;
        await AssertQualifiedPreviewAndApplyAsync(owner, caller, "BindingTarget.GetValue(v => 42, offset)");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_EquivalentNameOfBindingsPreviewAndApplyAcrossDocuments()
    {
        const string owner = """
            public class BindingOwner
            {
                public static int Read(int offset) =>
                    BindingTarget.GetValue(nameof(System.Math.Abs), nameof(System.Math), offset);
            }

            internal static class BindingTarget
            {
                internal static int GetValue(string method, string owner, int value) => method.Length + owner.Length + value + 35;
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset) =>
                    BindingTarget.GetValue(nameof(System.Math.Abs), nameof(System.Math), offset);
            }
            """;
        await AssertQualifiedPreviewAndApplyAsync(
            owner, caller, "BindingTarget.GetValue(nameof(System.Math.Abs), nameof(System.Math), offset)");
    }

    [TestMethod]
    public async Task ExtractSharedExpression_EquivalentArrayCollectionBindingsPreviewAndApplyAcrossDocuments()
    {
        const string owner = """
            public class BindingOwner
            {
                public static int Read(int offset) => BindingTarget.GetValue([offset]);
            }

            internal static class BindingTarget
            {
                internal static int GetValue(int[] values) => values[0] + 42;
            }
            """;
        const string caller = """
            public class BindingCaller
            {
                public static int Read(int offset) => BindingTarget.GetValue([offset]);
            }
            """;
        await AssertQualifiedPreviewAndApplyAsync(owner, caller, "BindingTarget.GetValue([offset])");
    }

    private static async Task AssertQualifiedPreviewAndApplyAsync(string owner, string caller, string expression)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "BindingOwner.cs");
        var callerPath = workspace.GetPath("SampleLib", "BindingCaller.cs");
        await File.WriteAllTextAsync(ownerPath, owner);
        await File.WriteAllTextAsync(callerPath, caller);
        var workspaceId = await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspaceId);
        await AssertBehaviorAsync(original, ownerPath, callerPath);

        var json = await PreviewAsync(workspaceId, ownerPath, owner, expression);
        using var preview = JsonDocument.Parse(json);
        Assert.AreEqual(2, preview.RootElement.GetProperty("changes").GetArrayLength(), json);
        var token = preview.RootElement.GetProperty("previewToken").GetString()!;
        var pending = PreviewStore.Retrieve(token);
        Assert.IsNotNull(pending);
        await AssertBehaviorAsync(pending.Value.ModifiedSolution, ownerPath, callerPath);

        var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
            WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
        using var applied = JsonDocument.Parse(appliedJson);
        Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
        await AssertBehaviorAsync(WorkspaceManager.GetCurrentSolution(workspaceId), ownerPath, callerPath);
    }

    private static async Task AssertBindingRefusalAsync(string owner, string caller, string expression)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "BindingOwner.cs");
        await File.WriteAllTextAsync(ownerPath, owner);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "BindingCaller.cs"), caller);
        var workspaceId = await workspace.LoadAsync();
        var compilation = await GetCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        AssertNoCompilerErrors(compilation);
        await AssertNoBindingPreviewAsync(workspaceId, ownerPath, owner, expression);
    }

    private static void AssertNoCompilerErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.IsFalse(errors.Any(), string.Join(Environment.NewLine, errors));
    }

    private static async Task AssertNoBindingPreviewAsync(string workspaceId, string ownerPath, string owner, string expression)
    {
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
            () => PreviewAsync(workspaceId, ownerPath, owner, expression));
        StringAssert.Contains(error.PublicMessage, "binding");
        FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
    }

    private static Task<string> PreviewAsync(string workspaceId, string ownerPath, string source, string expression)
    {
        var selection = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ExpressionSyntax>().Single(node => node.ToString() == expression);
        var span = selection.GetLocation().GetLineSpan();
        return ExtractMethodTools.PreviewExtractSharedExpressionToHelper(
            WorkspaceExecutionGate, ExtractMethodService, workspaceId, ownerPath,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
            "SharedValue", "internal", allowCrossFile: true);
    }

    private static async Task<Compilation> GetCompilationAsync(Solution solution)
    {
        var compilation = await solution.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        return compilation;
    }

    private static async Task AssertBehaviorAsync(Solution solution, string ownerPath, string callerPath)
    {
        var compilation = await GetCompilationAsync(solution);
        foreach (var path in new[] { ownerPath, callerPath })
        {
            var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
            var reparsed = CSharpSyntaxTree.ParseText(await tree.GetTextAsync(), (CSharpParseOptions)tree.Options, path);
            compilation = compilation.ReplaceSyntaxTree(tree, reparsed);
        }

        AssertEmittedResults(compilation, ("BindingOwner", 49), ("BindingCaller", 49));
    }

    private static void AssertEmittedResults(Compilation compilation, params (string TypeName, int Result)[] expected)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(nameof(ExtractSharedExpressionBindingTests), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            foreach (var (typeName, result) in expected)
            {
                var method = assembly.GetType(typeName)?.GetMethod("Read");
                Assert.IsNotNull(method);
                Assert.AreEqual(result, method.Invoke(null, [7]));
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
