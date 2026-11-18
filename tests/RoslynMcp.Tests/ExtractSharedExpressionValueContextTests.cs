using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Use-site context of <c>extract_shared_expression_to_helper_preview</c>: occurrences that need storage, a
/// compile-time constant, or an expression tree cannot be replaced by a value-returning helper call, and a
/// preview that would not compile or would silently re-bind is refused before any token exists.
/// </summary>
[TestClass]
public sealed class ExtractSharedExpressionValueContextTests : IsolatedWorkspaceTestBase
{
    private const string WritableMessage = "writable variable or by-reference storage";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private const string TargetSource = """
        public struct ValueCounter
        {
            public int N;
            public void Increment() { N++; }
        }

        public static class ValueContextTarget
        {
            public static int Field = 40;
            public static int Value { get; set; } = 2;
            public static ValueCounter Count;
            public static void Bump(ref int target) { target++; }
            public static int Peek(in int value) => value + 1;
        }

        """;

    // Each case pairs a value read with a structurally identical occurrence that requires storage.
    [TestMethod]
    [DataRow("assignment", "ValueContextTarget.Value",
        "public static int Read() { return ValueContextTarget.Value; }\n    public static void Write() { ValueContextTarget.Value = 5; }")]
    [DataRow("compound assignment", "ValueContextTarget.Value",
        "public static int Read() { return ValueContextTarget.Value; }\n    public static void Write() { ValueContextTarget.Value += 5; }")]
    [DataRow("increment", "ValueContextTarget.Value",
        "public static int Read() { return ValueContextTarget.Value; }\n    public static void Write() { ValueContextTarget.Value++; }")]
    [DataRow("ref argument", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static void Write() { ValueContextTarget.Bump(ref ValueContextTarget.Field); }")]
    [DataRow("out argument", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static bool Write() { return int.TryParse(\"1\", out ValueContextTarget.Field); }")]
    // An explicit `in` modifier needs a variable too (CS8156 for a call); only an implicit by-in argument takes a value.
    [DataRow("in argument keyword", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static int Write() { return ValueContextTarget.Peek(in ValueContextTarget.Field); }")]
    // Compiles and silently mutates a copy: a non-readonly mutating member on a struct storage receiver.
    [DataRow("mutating struct receiver", "ValueContextTarget.Count",
        "public static ValueCounter Read() { return ValueContextTarget.Count; }\n    public static void Write() { ValueContextTarget.Count.Increment(); }")]
    public async Task WritableTarget_RefusesBeforeToken_ForExampleOnEitherSide(string label, string expression, string members)
    {
        var source = TargetSource + "public static class WritableCase\n{\n    " + members + "\n}\n";
        foreach (var exampleOccurrence in new[] { 0, 1 })
        {
            await WithWorkspaceAsync(source, async (workspaceId, path) =>
            {
                var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
                    () => PreviewAsync(workspaceId, path, source, expression, exampleOccurrence));
                StringAssert.Contains(error.PublicMessage, WritableMessage,
                    $"{label}, example occurrence {exampleOccurrence}: {error.PublicMessage}");
                FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
            });
        }
    }

    [TestMethod]
    public async Task ReadOnlyOccurrences_PreviewApplyAndKeepBehavior()
    {
        var source = TargetSource + """
            public static class ReadRunner
            {
                public static int One() { int a = ValueContextTarget.Field; return a; }
                public static int Two() { return ValueContextTarget.Peek(ValueContextTarget.Field); }
                public static int Run() => One() + Two();
            }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            Assert.AreEqual(81, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "ReadRunner"));
            var json = await PreviewAsync(workspaceId, path, source, "ValueContextTarget.Field", 0);
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            var pending = PreviewStore.Retrieve(token);
            Assert.IsNotNull(pending);
            StringAssert.Contains((await GetDocumentTextAsync(pending.Value.ModifiedSolution, path)), "ValueContextTarget.Peek(SharedValue())");
            Assert.AreEqual(81, await RunAsync(pending.Value.ModifiedSolution, "ReadRunner"));

            var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
            using var applied = JsonDocument.Parse(appliedJson);
            Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
            Assert.AreEqual(81, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "ReadRunner"));
        });
    }

    private const string TreeSource = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;

        public static class TreeTarget
        {
            public static int GetValue(int offset) => offset + 42;
        }

        public static class TreeCase
        {
            public static Expression<Func<int>> TreeA() => () => TreeTarget.GetValue(7);
            public static Expression<Func<int>> TreeB() => () => TreeTarget.GetValue(7);
            public static Expression<Func<Func<int>>> TreeNested() => () => () => TreeTarget.GetValue(7);
            public static Func<int> DelegateA() => () => TreeTarget.GetValue(7);
            public static Func<int> DelegateB() => () => TreeTarget.GetValue(7);
            public static IQueryable<int> QueryClause() =>
                from x in new[] { 1, 2 }.AsQueryable() where x != TreeTarget.GetValue(7) select x;
            public static IQueryable<int> QueryMethod() =>
                new[] { 1, 2 }.AsQueryable().Where(x => x != TreeTarget.GetValue(7));

            public static int Run()
            {
                var sum = DelegateA()() + DelegateB()();
                sum += TreeA().Body is MethodCallExpression { Method.Name: "GetValue" } ? 1 : 0;
                sum += TreeB().Body is MethodCallExpression { Method.Name: "GetValue" } ? 1 : 0;
                sum += TreeNested().Body is LambdaExpression { Body: MethodCallExpression { Method.Name: "GetValue" } } ? 1 : 0;
                sum += QueryClause().Expression.ToString().Contains("GetValue(7)") ? 1 : 0;
                sum += QueryMethod().Expression.ToString().Contains("GetValue(7)") ? 1 : 0;
                return sum;
            }
        }
        """;

    // Occurrence order in TreeSource: 0 TreeA, 1 TreeB, 2 nested lambda inside a tree, 3 DelegateA, 4 DelegateB,
    // 5 implicit query-clause lambda over IQueryable, 6 explicit lambda over IQueryable.
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(5)]
    public async Task ExpressionTreeOccurrences_StayUnrewritten_DelegateOccurrencesAreExtracted(int exampleOccurrence)
    {
        await WithWorkspaceAsync(TreeSource, async (workspaceId, path) =>
        {
            const string call = "TreeTarget.GetValue(7)";
            Assert.AreEqual(103, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "TreeCase"));
            var json = await PreviewAsync(workspaceId, path, TreeSource, call, exampleOccurrence);
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            var pending = PreviewStore.Retrieve(token);
            Assert.IsNotNull(pending);
            AssertTreesUnchanged(await GetDocumentTextAsync(pending.Value.ModifiedSolution, path));
            Assert.AreEqual(103, await RunAsync(pending.Value.ModifiedSolution, "TreeCase"));

            var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
            using var applied = JsonDocument.Parse(appliedJson);
            Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
            AssertTreesUnchanged(await File.ReadAllTextAsync(path));
            Assert.AreEqual(103, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "TreeCase"));
        });
    }

    private static void AssertTreesUnchanged(string text)
    {
        foreach (var kept in new[]
        {
            "TreeA() => () => TreeTarget.GetValue(7);",
            "TreeB() => () => TreeTarget.GetValue(7);",
            "TreeNested() => () => () => TreeTarget.GetValue(7);",
            "where x != TreeTarget.GetValue(7) select x;",
            "Where(x => x != TreeTarget.GetValue(7));",
        })
        {
            StringAssert.Contains(text, kept);
        }

        StringAssert.Contains(text, "DelegateA() => () => SharedValue();");
        StringAssert.Contains(text, "DelegateB() => () => SharedValue();");
    }

    [TestMethod]
    public async Task OnlyExpressionTreeOccurrences_AreRefusedWithAnExpressionTreeReason()
    {
        const string source = """
            using System;
            using System.Linq.Expressions;

            public static class TreeTarget { public static int GetValue(int offset) => offset + 42; }

            public static class OnlyTrees
            {
                public static Expression<Func<int>> A() => () => TreeTarget.GetValue(7);
                public static Expression<Func<int>> B() => () => TreeTarget.GetValue(7);
            }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
                () => PreviewAsync(workspaceId, path, source, "TreeTarget.GetValue(7)", 0));
            StringAssert.Contains(error.PublicMessage, "expression tree");
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
        });
    }

    private static async Task WithWorkspaceAsync(string source, Func<string, string, Task> body)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ValueContextProbe.cs");
        await File.WriteAllTextAsync(path, source);
        var workspaceId = await workspace.LoadAsync();
        await body(workspaceId, path);
    }

    private static Task<string> PreviewAsync(
        string workspaceId, string path, string source, string expression, int occurrence,
        string accessibility = "internal")
    {
        var selection = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ExpressionSyntax>().Where(node => node.ToString() == expression).ElementAt(occurrence);
        var span = selection.GetLocation().GetLineSpan();
        return ExtractMethodTools.PreviewExtractSharedExpressionToHelper(
            WorkspaceExecutionGate, ExtractMethodService, workspaceId, path,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1,
            "SharedValue", accessibility, allowCrossFile: true);
    }

    private static async Task<string> GetDocumentTextAsync(Solution solution, string path)
    {
        var document = solution.Projects.SelectMany(project => project.Documents)
            .Single(document => string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase));
        return (await document.GetTextAsync()).ToString();
    }

    private static async Task<Compilation> GetCleanCompilationAsync(Solution solution)
    {
        var compilation = await solution.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        return compilation;
    }

    /// <summary>Compiles the solution's SampleLib (it must be error free), emits it and calls <c>typeName.Run()</c>.</summary>
    private static async Task<int> RunAsync(Solution solution, string typeName)
    {
        var compilation = await GetCleanCompilationAsync(solution);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(nameof(ExtractSharedExpressionValueContextTests), isCollectible: true);
        try
        {
            var method = context.LoadFromStream(stream).GetType(typeName)?.GetMethod("Run");
            Assert.IsNotNull(method, typeName);
            return (int)method.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
