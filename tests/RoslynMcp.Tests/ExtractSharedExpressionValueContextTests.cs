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
            public int Peek() => N;
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
    // Deliberate: a non-readonly struct member is refused as a receiver even when it does not mutate, because
    // whether it mutates is not decided here and the helper would pass a copy.
    [DataRow("non-readonly struct member receiver", "ValueContextTarget.Count",
        "public static ValueCounter Read() { return ValueContextTarget.Count; }\n    public static int Write() { return ValueContextTarget.Count.Peek(); }")]
    [DataRow("pre-decrement", "ValueContextTarget.Value",
        "public static int Read() { return ValueContextTarget.Value; }\n    public static void Write() { --ValueContextTarget.Value; }")]
    [DataRow("address-of", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static unsafe int* Write() { return &ValueContextTarget.Field; }")]
    [DataRow("ref expression", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static ref int Write() { return ref ValueContextTarget.Field; }")]
    [DataRow("deconstruction target", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static void Write() { (ValueContextTarget.Field, ValueContextTarget.Value) = (1, 2); }")]
    [DataRow("nested deconstruction target", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static void Write() { ((ValueContextTarget.Field, ValueContextTarget.Value), _) = ((1, 2), 3); }")]
    [DataRow("foreach deconstruction variable", "ValueContextTarget.Field",
        "public static int Read() { return ValueContextTarget.Field; }\n    public static void Write() { foreach ((ValueContextTarget.Field, ValueContextTarget.Value) in new[] { (1, 2) }) { } }")]
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

    // One row per constant-only slot the classifier knows. Each row's slot syntax is reached by exactly one
    // classifier arm, so deleting that arm turns the row red. Occurrence 0 is an ordinary `var q = 6 * 7;`.
    [TestMethod]
    [DataRow("case label",
        "public static int Switch(int x) { switch (x) { case 6 * 7: return 1; default: return 0; } }", "")]
    [DataRow("constant pattern", "public static bool Is(int x) => x is 6 * 7;", "")]
    [DataRow("relational pattern", "public static bool Rel(int x) => x is > 6 * 7;", "")]
    [DataRow("goto case",
        "public static int Goto(int x) { switch (x) { case 1: goto case 6 * 7; case 42: return 1; default: return 0; } }", "")]
    [DataRow("attribute argument", "[Slot(6 * 7)] public static void Marked() { }",
        "public sealed class SlotAttribute : System.Attribute { public SlotAttribute(int value) { } }")]
    [DataRow("parameter default value", "public static int Default(int p = 6 * 7) => p;", "")]
    [DataRow("const local", "public static int ConstLocal() { const int c = 6 * 7; return c; }", "")]
    [DataRow("const field", "public const int ConstField = 6 * 7;", "")]
    public async Task ConstantOnlySlots_AreRefusedBeforeToken_ForExampleOnEitherSide(string label, string members, string extra)
    {
        var source = "public static class ConstantSlots\n{\n    public static int Plain() { var q = 6 * 7; return q; }\n    " +
            members + "\n}\n" + extra + "\n";
        foreach (var exampleOccurrence in new[] { 0, 1 })
        {
            await WithWorkspaceAsync(source, async (workspaceId, path) =>
            {
                var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
                    () => PreviewAsync(workspaceId, path, source, "6 * 7", exampleOccurrence));
                StringAssert.Contains(error.PublicMessage, "compile-time constant",
                    $"{label}, example occurrence {exampleOccurrence}: {error.PublicMessage}");
                FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
            });
        }
    }

    // The semantic model reports no type or constant for a fixed-size buffer length: as the example it has no
    // inferable return type, as a candidate it is not provably equivalent to the others. Either way the
    // extraction is refused before a token and the occurrence is never rewritten.
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task FixedBufferLength_IsRefusedBeforeToken_ByTheBindingCheck(int exampleOccurrence)
    {
        const string source = """
            public static class FixedSlots
            {
                public static int Plain() { var q = 6 * 7; return q; }
            }

            public unsafe struct FixedHolder { public fixed int Buffer[6 * 7]; }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => PreviewAsync(workspaceId, path, source, "6 * 7", exampleOccurrence));
            StringAssert.Contains(error.Message, exampleOccurrence == 0 ? "bindings" : "return type");
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
        });
    }

    // An enum declaration is not a type declaration, so its member values are never candidates (and need no
    // constant-slot rule): they stay as written while the other two occurrences are extracted.
    [TestMethod]
    public async Task EnumMemberInitializer_IsNeverACandidate_AndStaysUnchanged()
    {
        const string source = """
            public static class EnumSlots
            {
                public static int Plain() { var q = 6 * 7; return q; }
                public static int Plain2() { var q = 6 * 7; return q; }
            }

            public enum SlottedEnum { A = 6 * 7 }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            var json = await PreviewAsync(workspaceId, path, source, "6 * 7", 0);
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            var text = await GetDocumentTextAsync(PreviewStore.Retrieve(token)!.Value.ModifiedSolution, path);
            StringAssert.Contains(text, "SlottedEnum { A = 6 * 7 }");
            StringAssert.Contains(text, "var q = SharedValue();");
        });
    }

    // One row per query clause that becomes an Expression<> lambda over IQueryable. The clause occurrence stays
    // unchanged while the two delegate occurrences are extracted.
    [TestMethod]
    [DataRow("let", "from x in S let y = TreeTarget.GetValue(7) select x + y")]
    [DataRow("orderby", "from x in S orderby x + TreeTarget.GetValue(7) select x")]
    [DataRow("select", "from x in S select x + TreeTarget.GetValue(7)")]
    [DataRow("join left key", "from x in S join y in S on x + TreeTarget.GetValue(7) equals y select x")]
    [DataRow("join right key", "from x in S join y in S on x equals y + TreeTarget.GetValue(7) select x")]
    [DataRow("group by key", "from x in S group x by x + TreeTarget.GetValue(7)")]
    [DataRow("group element", "from x in S group x + TreeTarget.GetValue(7) by x")]
    [DataRow("from selector", "from x in S from y in new[] { TreeTarget.GetValue(7) } select x + y")]
    public async Task QueryClauseLambdas_StayUnrewritten_ForExampleOnEitherSide(string label, string query)
    {
        var member = "public static object Q() => " + query + ";";
        var source = """
            using System;
            using System.Linq;

            public static class TreeTarget { public static int GetValue(int offset) => offset + 42; }

            public static class QueryArms
            {
                private static IQueryable<int> S => new[] { 1, 2 }.AsQueryable();

            """ + "    " + member + """

                public static Func<int> DelegateA() => () => TreeTarget.GetValue(7);
                public static Func<int> DelegateB() => () => TreeTarget.GetValue(7);
            }
            """;
        foreach (var exampleOccurrence in new[] { 0, 1 })
        {
            await WithWorkspaceAsync(source, async (workspaceId, path) =>
            {
                var json = await PreviewAsync(workspaceId, path, source, "TreeTarget.GetValue(7)", exampleOccurrence);
                using var preview = JsonDocument.Parse(json);
                var token = preview.RootElement.GetProperty("previewToken").GetString()!;
                var text = await GetDocumentTextAsync(PreviewStore.Retrieve(token)!.Value.ModifiedSolution, path);
                StringAssert.Contains(text, member, $"{label}, example occurrence {exampleOccurrence}: query clause was rewritten");
                StringAssert.Contains(text, "DelegateA() => () => SharedValue();", label);
                StringAssert.Contains(text, "DelegateB() => () => SharedValue();", label);
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

    // Occurrences of `6 * 7`: 0 is the constant-only interpolation alignment, 1 and 2 are ordinary expressions.
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task ConstantOnlyAlignment_IsRefusedBeforeToken(int exampleOccurrence)
    {
        const string source = """
            public static class ConstantCase
            {
                public static string Aligned() => $"{1,6 * 7}|";
                public static int Plain() { var q = 6 * 7; return q; }
                public static int Plain2() { var q = 6 * 7; return q; }
            }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
                () => PreviewAsync(workspaceId, path, source, "6 * 7", exampleOccurrence));
            StringAssert.Contains(error.PublicMessage, "compile-time constant");
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
        });
    }

    // Replacing a constant keeps working where its ancestors merely lose constness (`int x = 6 * 7 + 1;`).
    [TestMethod]
    public async Task OrdinaryConstantArithmetic_PreviewAndApplyKeepBehavior()
    {
        const string source = """
            public static class OrdinaryConstants
            {
                public static int A() { int x = 6 * 7 + 1; return x; }
                public static int B() { int y = 6 * 7 + 1; return y; }
                public static int Run() => A() + B();
            }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            Assert.AreEqual(86, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "OrdinaryConstants"));
            var json = await PreviewAsync(workspaceId, path, source, "6 * 7", 0);
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            var pending = PreviewStore.Retrieve(token);
            Assert.IsNotNull(pending);
            StringAssert.Contains(await GetDocumentTextAsync(pending.Value.ModifiedSolution, path), "int x = SharedValue() + 1;");
            Assert.AreEqual(86, await RunAsync(pending.Value.ModifiedSolution, "OrdinaryConstants"));
            var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
            using var applied = JsonDocument.Parse(appliedJson);
            Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
            Assert.AreEqual(86, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "OrdinaryConstants"));
        });
    }

    // The enclosing constant `(6 * 7) + 1` converts to byte; with the helper call it stops being constant and the
    // call silently re-binds to Bind(long), which still compiles.
    [TestMethod]
    public async Task LostConstantConversion_ThatReBindsAnOverload_IsRefusedBeforeToken()
    {
        const string source = """
            public static class OverloadCase
            {
                private static int Bind(byte value) => 1;
                private static int Bind(long value) => 2;
                public static int A() => Bind((6 * 7) + 1);
                public static int B() => Bind((6 * 7) + 1);
                public static int Run() => A() + B();
            }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            Assert.AreEqual(2, await RunAsync(WorkspaceManager.GetCurrentSolution(workspaceId), "OverloadCase"));
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
                () => PreviewAsync(workspaceId, path, source, "6 * 7", 0));
            StringAssert.Contains(error.PublicMessage, "changes how the surrounding expression binds");
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
        });
    }

    private const string OwnerSource = """
        public class HelperOwner
        {
            public int Read(int input) => input * 31;
        }
        """;

    private const string CallerSource = """
        public class HelperCaller
        {
            public int Read(int input) => input * 31;
        }
        """;

    [TestMethod]
    public async Task PrivateHelperAcrossTypes_IsRefusedBeforeToken()
    {
        await WithTwoDocumentsAsync(OwnerSource, CallerSource, async (workspaceId, ownerPath) =>
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
                () => PreviewAsync(workspaceId, ownerPath, OwnerSource, "input * 31", 0, accessibility: "private"));
            StringAssert.Contains(error.PublicMessage, "introduce compiler errors");
            StringAssert.Contains(error.PublicMessage, "CS0122");
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, workspaceId);
        });
    }

    [TestMethod]
    public async Task InternalHelperAcrossTypes_PreviewAndApplyCompile()
    {
        await WithTwoDocumentsAsync(OwnerSource, CallerSource, async (workspaceId, ownerPath) =>
        {
            var json = await PreviewAsync(workspaceId, ownerPath, OwnerSource, "input * 31", 0, accessibility: "internal");
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            await GetCleanCompilationAsync(PreviewStore.Retrieve(token)!.Value.ModifiedSolution);
            var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
            using var applied = JsonDocument.Parse(appliedJson);
            Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
            await GetCleanCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        });
    }

    // The compiler, not a syntactic rule, decides private access: a type nested in the owner can call the helper.
    [TestMethod]
    public async Task PrivateHelper_CallerNestedInOwner_PreviewAndApplyCompile()
    {
        const string source = """
            public class NestedOwner
            {
                public int Read(int input) => input * 31;

                public class Inner
                {
                    public int Read(int input) => input * 31;
                }
            }
            """;
        await WithWorkspaceAsync(source, async (workspaceId, path) =>
        {
            var json = await PreviewAsync(workspaceId, path, source, "input * 31", 0, accessibility: "private");
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            await GetCleanCompilationAsync(PreviewStore.Retrieve(token)!.Value.ModifiedSolution);
            var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
            using var applied = JsonDocument.Parse(appliedJson);
            Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
            await GetCleanCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        });
    }

    // The example occurrence sits in an expression tree and stays unchanged; the only rewritten sites are in another
    // document. The helper is still inserted, exactly once, into the example's type.
    [TestMethod]
    public async Task ExampleLeftUnchangedInExpressionTree_HelperIsInsertedOnceIntoExampleType()
    {
        const string exampleSource = """
            using System;
            using System.Linq.Expressions;

            public static class TreeTarget
            {
                public static int GetValue(int offset) => offset + 42;
            }

            public static class ExampleHost
            {
                public static Expression<Func<int>> Tree() => () => TreeTarget.GetValue(7);
            }
            """;
        const string otherSource = """
            public static class DelegateUser
            {
                public static int A() => TreeTarget.GetValue(7);
                public static int B() => TreeTarget.GetValue(7);
            }
            """;
        await WithTwoDocumentsAsync(exampleSource, otherSource, async (workspaceId, examplePath) =>
        {
            var json = await PreviewAsync(workspaceId, examplePath, exampleSource, "TreeTarget.GetValue(7)", 0);
            using var preview = JsonDocument.Parse(json);
            var token = preview.RootElement.GetProperty("previewToken").GetString()!;
            var projected = PreviewStore.Retrieve(token)!.Value.ModifiedSolution;

            var helpers = new List<MethodDeclarationSyntax>();
            foreach (var document in projected.Projects.Single(project => project.Name == "SampleLib").Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                helpers.AddRange(root!.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Identifier.ValueText == "SharedValue"));
            }

            Assert.HasCount(1, helpers, "The helper must be declared exactly once.");
            Assert.AreEqual("ExampleHost", ((TypeDeclarationSyntax)helpers[0].Parent!).Identifier.ValueText);

            var exampleText = await GetDocumentTextAsync(projected, examplePath);
            StringAssert.Contains(exampleText, "Tree() => () => TreeTarget.GetValue(7);");
            var otherText = await GetDocumentTextAsync(projected, examplePath.Replace("HelperOwner.cs", "HelperCaller.cs"));
            StringAssert.Contains(otherText, "A() => global::ExampleHost.SharedValue();");
            StringAssert.Contains(otherText, "B() => global::ExampleHost.SharedValue();");
            await GetCleanCompilationAsync(projected);

            var appliedJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate, ApplyUndoWorkflowService, PreviewStore, token);
            using var applied = JsonDocument.Parse(appliedJson);
            Assert.AreEqual("applied", applied.RootElement.GetProperty("status").GetString(), appliedJson);
            await GetCleanCompilationAsync(WorkspaceManager.GetCurrentSolution(workspaceId));
        });
    }

    private static async Task WithTwoDocumentsAsync(string ownerSource, string callerSource, Func<string, string, Task> body)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var ownerPath = workspace.GetPath("SampleLib", "HelperOwner.cs");
        await File.WriteAllTextAsync(ownerPath, ownerSource);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "HelperCaller.cs"), callerSource);
        var workspaceId = await workspace.LoadAsync();
        await body(workspaceId, ownerPath);
    }

    private static async Task WithWorkspaceAsync(string source, Func<string, string, Task> body)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "ValueContextProbe.cs");
        await File.WriteAllTextAsync(path, source);
        // Some rows need `unsafe` code (address-of, fixed-size buffers).
        var project = workspace.GetPath("SampleLib", "SampleLib.csproj");
        await File.WriteAllTextAsync(project, (await File.ReadAllTextAsync(project)).Replace(
            "<TargetFramework>net10.0</TargetFramework>",
            "<TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks>", StringComparison.Ordinal));
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
