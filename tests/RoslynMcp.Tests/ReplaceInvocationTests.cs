namespace RoslynMcp.Tests;

/// <summary>
/// replace-invocation-pattern-refactor: the new tool rewrites every call-site of an old
/// method to invoke a new method whose parameter list is a permutation of the old. The
/// reorder is driven by parameter-name equality so the caller only needs to declare the FQ
/// signatures and the rewriter derives the index mapping. Audit F#2 (2026-04-15) — the
/// concrete scenario that motivated this tool — had callers of
/// <c>Build(a, b, c)</c> migrate to <c>Generate(b, c, a)</c> with argument reorder; the
/// fixture below replays that shape and asserts every call-form is rewritten correctly.
/// </summary>
// donotparallelize-audit-wave-23: [DoNotParallelize] removed. Every test method creates its own
// per-test SampleSolution copy (CreateSampleSolutionCopy), writes fixtures only inside that copy,
// loads/closes it via WorkspaceManager (ConcurrentDictionary session table + slot limiter, Strict
// evict policy with a test cap of 64 — no LRU eviction of other classes' sessions), and only calls
// the preview side of BulkRefactoringService — no *_apply, no reload of a shared workspace, no
// UndoService/ChangeTracker writes. Validated by a bounded repeated (3x) concurrent run alongside
// its wave-23 siblings and parallel-enabled workspace-loading classes, green every time.
[TestClass]
public sealed class ReplaceInvocationTests : SharedWorkspaceTestBase
{
    [TestMethod]
    public async Task ReplaceInvocation_ArgumentRefusals_ArePublicAndBounded()
    {
        foreach (var parameter in new[] { "oldMethod", "newMethod" })
        {
            foreach (var input in new string?[] { null, "", "   " })
                await BulkRefactoringTests.AssertPublicRefusalAsync(
                    () => BulkRefactoringService.PreviewReplaceInvocationAsync("unused",
                        parameter == "oldMethod" ? input! : "M()",
                        parameter == "newMethod" ? input! : "M()", null, CancellationToken.None),
                    "replace_invocation_preview", parameter,
                    parameter + " must be a fully-qualified signature like 'Type.Method(P1,P2)'.");
            await BulkRefactoringTests.AssertPublicRefusalAsync(
                () => BulkRefactoringService.PreviewReplaceInvocationAsync("unused",
                    parameter == "oldMethod" ? " () " : "M()", parameter == "newMethod" ? " () " : "M()", null, CancellationToken.None),
                "replace_invocation_preview", parameter, "Method signature must include a non-empty method name.");
        }
        await BulkRefactoringTests.AssertPublicRefusalAsync(
            () => BulkRefactoringService.PreviewReplaceInvocationAsync("unused", "M()", "N()", "C:/private/scope-secret", CancellationToken.None),
            "replace_invocation_preview", "scope", "scope must be 'all' for replace_invocation_preview.", "C:/private/scope-secret");
    }

    [TestMethod]
    [DataRow("C:/private/signature-secret")]
    [DataRow("M(int)C:/private/trailing")]
    [DataRow("M(,int)")]
    [DataRow("M(int,)")]
    [DataRow("M(int,,string)")]
    [DataRow("M(List<int)")]
    [DataRow("M((int,string)")]
    [DataRow("M(int[)")]
    [DataRow("M(int])")]
    [DataRow("M(int>)")]
    [DataRow("M(())")]
    [DataRow("M(int string)")]
    [DataRow("M(int;)")]
    [DataRow("M(List<>)")]
    [DataRow("M(int))")]
    [DataRow("M(int) /* C:/private/comment */")]
    public async Task ReplaceInvocation_InvalidGrammar_PublishesRefusal(string input)
    {
        foreach (var parameter in new[] { "oldMethod", "newMethod" })
            await BulkRefactoringTests.AssertPublicRefusalAsync(
                () => BulkRefactoringService.PreviewReplaceInvocationAsync("unused",
                    parameter == "oldMethod" ? input : "M()", parameter == "newMethod" ? input : "M()", null, CancellationToken.None),
                "replace_invocation_preview", parameter,
                "Method signature must have the form 'Namespace.Type.Method(ParamType1, ParamType2)' with complete, non-empty parameter types.", input);
    }

    [TestMethod]
    [DataRow("SampleLib.NestedSignature.Old(Dictionary<string, List<int>>)", "SampleLib.NestedSignature.New(Dictionary<string, List<int>>)")]
    [DataRow("SampleLib.NestedSignature.Old( (int, string) )", "SampleLib.NestedSignature.New( (int, string) )")]
    [DataRow("SampleLib.NestedSignature.Old(int[,])", "SampleLib.NestedSignature.New(int[,])")]
    [DataRow(" EmptyOld( ) ", " EmptyNew( ) ")]
    [DataRow("SampleLib.NestedSignature.ShortOld(value)", "SampleLib.NestedSignature.ShortNew(value)")]
    [DataRow("SampleLib.NestedSignature+Inner.Old(int)", "SampleLib.NestedSignature+Inner.New(int)")]
    public async Task ReplaceInvocation_NestedTypes_ResolveActualOverloads(string oldName, string newName)
    {
        var path = CreateSampleSolutionCopy();
        var fixture = Path.Combine(Path.GetDirectoryName(path)!, "SampleLib", "NestedSignatureFixture.cs");
        await File.WriteAllTextAsync(fixture, """
            using System.Collections.Generic;
            namespace SampleLib;
            public static class NestedSignature
            {
                public static int Old(Dictionary<string, List<int>> value) => 1;
                public static int Old((int, string) value) => 2;
                public static int Old(int[,] value) => 3;
                public static int New(Dictionary<string, List<int>> value) => 1;
                public static int New((int, string) value) => 2;
                public static int New(int[,] value) => 3;
                public static int EmptyOld() => 4;
                public static int EmptyNew() => 4;
                public static int ShortOld(int value) => 5;
                public static int ShortNew(int value) => 5;
                public class Inner
                {
                    public static int Old(int value) => 6;
                    public static int New(int value) => 6;
                }
                public static int Calls() => Old(new Dictionary<string, List<int>>()) +
                    Old((1, "x")) + Old(new int[1,1]) + EmptyOld() + ShortOld(1) + Inner.Old(1);
            }
            """);
        var loaded = await WorkspaceManager.LoadAsync(path, CancellationToken.None);
        try
        {
            var preview = await BulkRefactoringService.PreviewReplaceInvocationAsync(loaded.WorkspaceId, oldName, newName, null, CancellationToken.None);
            Assert.AreEqual(1, preview.CallsiteUpdates!.Sum(x => x.CallsiteCount), oldName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }


    [TestMethod]
    [DataRow("optional")]
    [DataRow("mixed")]
    public async Task ReplaceInvocation_NewNestedReachability_PreservesArgumentBindings(string shape)
    {
        var path = CreateSampleSolutionCopy();
        var fixture = Path.Combine(Path.GetDirectoryName(path)!, "SampleLib", "NestedBindingFixture.cs");
        await File.WriteAllTextAsync(fixture, """
            using System.Collections.Generic;
            namespace SampleLib;
            public static class NestedBinding
            {
                public static int OptionalOld(Dictionary<string, List<int>> value, int count = 1) => count;
                public static int OptionalOld(string value, int count = 1) => count;
                public static int OptionalNew(int count = 2, Dictionary<string, List<int>> value = null) => count;
                public static int OptionalNew(int count = 2, string value = null) => count;
                public static int MixedOld((int, string) value, int count) => count;
                public static int MixedOld(string value, int count) => count;
                public static int MixedNew(int count, (int, string) value) => count;
                public static int MixedNew(int count, string value) => count;
                public static int OptionalCall(Dictionary<string, List<int>> dictionary) => OptionalOld(dictionary);
                public static int MixedCall() => MixedOld(value: (1, "x"), 2);
                public static int ParamsOld(int first, params (int, string)[] rest) => rest.Length;
                public static int ParamsCall() => ParamsOld(1, (2, "y"), (3, "z"));
            }
            """);
        var loaded = await WorkspaceManager.LoadAsync(path, CancellationToken.None);
        try
        {
            var signatures = shape == "optional"
                ? ("SampleLib.NestedBinding.OptionalOld(Dictionary<string, List<int>>, int)", "SampleLib.NestedBinding.OptionalNew(int, Dictionary<string, List<int>>)")
                : ("SampleLib.NestedBinding.MixedOld((int, string), int)", "SampleLib.NestedBinding.MixedNew(int, (int, string))");
            var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
            var originalCompilation = await original.Projects.Single(x => x.Name == "SampleLib").GetCompilationAsync();
            Assert.IsNotNull(originalCompilation);
            Assert.IsFalse(originalCompilation.GetDiagnostics().Any(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                string.Join(Environment.NewLine, originalCompilation.GetDiagnostics()));

            var preview = await BulkRefactoringService.PreviewReplaceInvocationAsync(
                loaded.WorkspaceId, signatures.Item1, signatures.Item2, null, CancellationToken.None);
            var changed = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var compilation = await changed.Projects.Single(x => x.Name == "SampleLib").GetCompilationAsync();
            Assert.IsNotNull(compilation);
            Assert.IsFalse(compilation.GetDiagnostics().Any(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error),
                "Rewritten invocation must compile: " + string.Join(Environment.NewLine, compilation.GetDiagnostics()));
            var doc = changed.Projects.Single(x => x.Name == "SampleLib").Documents.Single(x => x.FilePath == fixture);
            var root = await doc.GetSyntaxRootAsync();
            var model = await doc.GetSemanticModelAsync();
            Assert.IsNotNull(root);
            Assert.IsNotNull(model);
            var invocation = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(x => x.Expression.ToString() == (shape == "optional" ? "OptionalNew" : "MixedNew"));
            var operation = model.GetOperation(invocation) as Microsoft.CodeAnalysis.Operations.IInvocationOperation;
            Assert.IsNotNull(operation);
            var count = operation.Arguments.Single(x => x.Parameter!.Name == "count");
            Assert.AreEqual(shape == "optional" ? 1 : 2, count.Value.ConstantValue.Value);
            Assert.AreEqual(2, operation.Arguments.Length, "Every original parameter remains represented.");
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }


    private static string ExecuteCompilation(Microsoft.CodeAnalysis.Compilation compilation, string method)
    {
        foreach (var tree in compilation.SyntaxTrees.ToArray())
            compilation = compilation.ReplaceSyntaxTree(tree, Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                tree.GetText(), (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)tree.Options, tree.FilePath));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext(Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("SampleLib.InvocationSemantics")!.GetMethod(method)!.Invoke(null, null)!;
        }
        finally { context.Unload(); }
    }

    [TestMethod]
    [DataRow("Order", "Old(int, int)", "New(int, int)")]
    [DataRow("Named", "Old(int, int)", "New(int, int)")]
    [DataRow("Mixed", "Old(int, int)", "New(int, int)")]
    [DataRow("Optional", "OptionalOld(int, int, string)", "OptionalNew(string, int, int)")]
    [DataRow("Caller", "CallerOld(int, string, string, int, string)", "CallerNew(string, string, int, string, int)")]
    [DataRow("Params", "ParamsOld(int, (int, string)[])", "ParamsNew((int, string)[], int)")]
    [DataRow("EmptyParams", "ParamsOld(int, (int, string)[])", "ParamsNew((int, string)[], int)")]
    [DataRow("ExplicitParams", "ParamsOld(int, (int, string)[])", "ParamsNew((int, string)[], int)")]
    [DataRow("CollectionParams", "CollectionOld(int, List<(int, string)>)", "CollectionNew(List<(int, string)>, int)")]
    [DataRow("Ref", "RefOld(int, int, int)", "RefNew(int, int, int)")]
    [DataRow("Keyword", "KeywordOld(int, int)", "KeywordNew(int, int)")]
    [DataRow("Nested", "NestedOld((int, string), int)", "NestedNew(int, (int, string))")]
    [DataRow("Generic", "GenericOld()", "GenericNew()")]
    [DataRow("ConditionalGeneric", "GenericInstanceOld()", "GenericInstanceNew()")]
    [DataRow("Extension", "ExtensionOld(int, int, int)", "ExtensionNew(int, int, int)")]
    [DataRow("DefaultTypes", "DefaultsOld(int, DayOfWeek, decimal, DateTime, int?)", "DefaultsNew(int?, DateTime, decimal, DayOfWeek, int)")]
    [DataRow("Conversion", "ConvertOld(int, int)", "ConvertNew(long, long)")]
    public async Task ReplaceInvocation_PreservesExecutableSemantics(string method, string oldSignature, string newSignature)
    {
        var path = CreateSampleSolutionCopy();
        var fixture = Path.Combine(Path.GetDirectoryName(path)!, "SampleLib", "InvocationSemantics.cs");
        await File.WriteAllTextAsync(fixture, """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            namespace SampleLib;
            public class InvocationSemantics
            {
                private static string Trace = "";
                private static int Step(int value) { Trace += value; return value; }
                public static int Old(int a, int b) => a * 10 + b;
                public static int New(int b, int a) => a * 10 + b;
                public static string Order() => Old(Step(1), /*comma*/ Step(2)) + ":" + Trace;
                public static string Named() => Old(b /*name*/ : Step(2), a: Step(1)) + ":" + Trace;
                public static string Mixed() => Old(a: Step(1), Step(2)) + ":" + Trace;
                public static string OptionalOld(int a, int b = 7, string text = "old") => a + ":" + b + ":" + text;
                public static string OptionalNew(string text = "new", int b = 8, int a = 0) => a + ":" + b + ":" + text;
                public static string Optional() => OptionalOld(1);
                public static string CallerOld(int value, [CallerArgumentExpression("value")] string expr = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0, [CallerFilePath] string file = "") => value + ":" + expr + ":" + member + ":" + line + ":" + file;
                public static string CallerNew(string file = "", string member = "", int line = 0, string expr = "", int value = 0) => value + ":" + expr + ":" + member + ":" + line + ":" + file;
                public static string Caller() => CallerOld(1 + 2);
                public static int ParamsOld(int first, params (int, string)[] rest) => first + rest.Length;
                public static int ParamsNew((int, string)[] rest, int first) => first + rest.Length;
                public static string Params() => ParamsOld(Step(1), (Step(2), "y"), /*element*/ (Step(3), "z")) + ":" + Trace;
                public static string EmptyParams() => ParamsOld(1).ToString();
                public static string ExplicitParams() => ParamsOld(1, new[] { (2, "y") }).ToString();
                public static int CollectionOld(int first, params List<(int, string)> rest) => first + rest.Count;
                public static int CollectionNew(List<(int, string)> rest, int first) => first + rest.Count;
                public static string CollectionParams() => CollectionOld(Step(1), (Step(2), "y"), (Step(3), "z")) + ":" + Trace;
                public static void RefOld(in int a, ref int b, out int c) { b += a; c = b * 2; }
                public static void RefNew(out int c, in int a, ref int b) { b += a; c = b * 2; }
                public static string Ref() { int a = 1, b = 2; RefOld(in a, ref b, out int c); return b + ":" + c; }
                public static int KeywordOld(int @event, int other) => @event * 10 + other;
                public static int KeywordNew(int other, int @event) => @event * 10 + other;
                public static string Keyword() => KeywordOld(@event: 1, other: 2).ToString();
                public static (int, string) NestedOld((int, string) value, int count) => (value.Item1 + count, value.Item2);
                public static (int, string) NestedNew(int count, (int, string) value) => (value.Item1 + count, value.Item2);
                public static string Nested() => NestedOld(NestedOld((1, "x"), 2), 3).ToString();
                public static string GenericOld<T>() => typeof(T).Name;
                public static string GenericNew<T>() => typeof(T).Name;
                public static string Generic() => InvocationSemantics.GenericOld<int>();
                public string GenericInstanceOld<T>() => typeof(T).Name;
                public string GenericInstanceNew<T>() => typeof(T).Name;
                public static string ConditionalGeneric() => new InvocationSemantics()?.GenericInstanceOld<int>() ?? "";
                public static long ConvertOld(int a, int b) => a * 10 + b;
                public static long ConvertNew(long b, long a) => a * 10 + b;
                public static string Conversion() => ConvertOld(Step(1), Step(2)) + ":" + Trace;
                public static string Extension() => Step(1).ExtensionOld(Step(2)) + ":" + Trace;
                public static string DefaultsOld(int value, DayOfWeek day = DayOfWeek.Monday, decimal amount = 2.5m, DateTime date = default, int? count = null) => value + ":" + day + ":" + amount + ":" + date.Ticks + ":" + count;
                public static string DefaultsNew(int? count = 9, DateTime date = default, decimal amount = 7m, DayOfWeek day = DayOfWeek.Friday, int value = 0) => value + ":" + day + ":" + amount + ":" + date.Ticks + ":" + count;
                public static string DefaultTypes() => DefaultsOld(1);
            }
            public static class InvocationExtensions
            {
                public static int ExtensionOld(this int receiver, int value, int count = 7) => receiver * 100 + value * 10 + count;
                public static int ExtensionNew(this int receiver, int count = 8, int value = 0) => receiver * 100 + value * 10 + count;
            }
            """);
        var loaded = await WorkspaceManager.LoadAsync(path, CancellationToken.None);
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
            var oldCompilation = await original.Projects.Single(x => x.Name == "SampleLib").GetCompilationAsync();
            Assert.IsNotNull(oldCompilation);
            var before = ExecuteCompilation(oldCompilation, method);
            var preview = await BulkRefactoringService.PreviewReplaceInvocationAsync(
                loaded.WorkspaceId, (method == "Extension" ? "SampleLib.InvocationExtensions." : "SampleLib.InvocationSemantics.") + oldSignature,
                (method == "Extension" ? "SampleLib.InvocationExtensions." : "SampleLib.InvocationSemantics.") + newSignature, null, CancellationToken.None);
            var modified = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var newCompilation = await modified.Projects.Single(x => x.Name == "SampleLib").GetCompilationAsync();
            Assert.IsNotNull(newCompilation);
            Assert.AreEqual(before, ExecuteCompilation(newCompilation, method), "Result and expression evaluation order must survive the rewrite.");
            var document = modified.Projects.Single(x => x.Name == "SampleLib").Documents.Single(x => x.FilePath == fixture);
            var text = (await document.GetTextAsync()).ToString();
            foreach (var marker in new[] { "/*comma*/", "/*name*/", "/*element*/" })
                StringAssert.Contains(text, marker);

            if (method == "Nested")
            {
                Assert.AreEqual(2, preview.CallsiteUpdates!.Sum(x => x.CallsiteCount));
                var syntax = await document.GetSyntaxRootAsync();
                var model = await document.GetSemanticModelAsync();
                var entry = syntax!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                    .Single(x => x.Identifier.ValueText == "Nested");
                var calls = entry.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                    .Where(x => model!.GetOperation(x) is Microsoft.CodeAnalysis.Operations.IInvocationOperation op &&
                        op.TargetMethod.Name.StartsWith("Nested", StringComparison.Ordinal)).ToArray();
                Assert.AreEqual(2, calls.Length);
                foreach (var call in calls)
                    Assert.AreEqual("NestedNew", ((Microsoft.CodeAnalysis.Operations.IInvocationOperation)model!.GetOperation(call)!).TargetMethod.Name);
            }
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("Drop", "Old(int, int)", "Drop(int)")]
    [DataRow("Incompatible", "Old(int, int)", "Incompatible(string, int)")]
    [DataRow("WrongOverload", "Old(int, int)", "WrongOverload(string, int)")]
    [DataRow("RefMode", "Old(int, int)", "RefMode(ref int, int)")]
    public async Task ReplaceInvocation_UnsafeMappings_RefuseBeforeMintingPreview(string shape, string oldSignature, string newSignature)
    {
        var path = CreateSampleSolutionCopy();
        var fixture = Path.Combine(Path.GetDirectoryName(path)!, "SampleLib", "RefusedMapping.cs");
        await File.WriteAllTextAsync(fixture, """
            namespace SampleLib;
            public static class RefusedMapping
            {
                public static int Old(int a, int b) => a + b;
                public static int Drop(int a) => a;
                public static int Incompatible(string b, int a) => a;
                public static int WrongOverload(string b, int a) => a;
                public static int WrongOverload(int b, int a) => a + b;
                public static int RefMode(ref int b, int a) => a + b;
                public static int Call() => Old(1, 2);
            }
            """);
        var loaded = await WorkspaceManager.LoadAsync(path, CancellationToken.None);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId);
            if (shape == "WrongOverload")
            {
                var compilation = await WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId).Projects
                    .Single(x => x.Name == "SampleLib").GetCompilationAsync();
                var overloads = compilation!.GetTypeByMetadataName("SampleLib.RefusedMapping")!
                    .GetMembers("WrongOverload").OfType<Microsoft.CodeAnalysis.IMethodSymbol>().ToArray();
                Assert.AreEqual(2, overloads.Length);
                var ids = overloads.Select(x => x.GetDocumentationCommentId()).ToArray();
                Assert.IsTrue(ids.All(x => x is not null));
                Assert.AreEqual(2, ids.Distinct(StringComparer.Ordinal).Count(),
                    "Canonical declaration IDs must distinguish the requested and accidentally bound overloads.");
            }
            var ex = await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(
                () => BulkRefactoringService.PreviewReplaceInvocationAsync(loaded.WorkspaceId,
                    "SampleLib.RefusedMapping." + oldSignature,
                    "SampleLib.RefusedMapping." + newSignature.Replace("ref ", ""),
                    null, CancellationToken.None));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId));
            StringAssert.Contains(ex.PublicMessage, shape == "Drop" ? "permutation" : shape == "RefMode" ? "passing mode" : "valid binding");
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Three call-forms exist in real code and must all be rewritten:
    ///   1. Positional:        Build(a, b, c)
    ///   2. Positional literals: Build(1, "x", true)
    ///   3. Named out-of-order: Build(arg3: true, arg1: 1, arg2: "x")
    ///
    /// After rewrite, every site invokes Generate with arguments bound to the corresponding
    /// parameter names. Original lexical order remains unchanged so expressions keep their
    /// evaluation order, including named and mixed argument forms.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_ReordersArgumentsAcrossCallForms()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ReplaceInvocationFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public static class ReplaceInvocationHelper
            {
                public static string Build(int arg1, string arg2, bool arg3) => $"{arg1}-{arg2}-{arg3}";
                public static string Generate(string arg2, bool arg3, int arg1) => $"{arg1}-{arg2}-{arg3}";
            }

            public static class ReplaceInvocationCallers
            {
                public static string Positional(int a, string b, bool c)
                {
                    return ReplaceInvocationHelper.Build(a, b, c);
                }

                public static string PositionalLiterals()
                {
                    return ReplaceInvocationHelper.Build(1, "x", true);
                }

                public static string NamedOutOfOrder()
                {
                    return ReplaceInvocationHelper.Build(arg3: true, arg1: 1, arg2: "x");
                }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var scopedWorkspaceId = loadResult.WorkspaceId;

        try
        {
            var preview = await BulkRefactoringService.PreviewReplaceInvocationAsync(
                scopedWorkspaceId,
                oldMethod: "SampleLib.ReplaceInvocationHelper.Build(int, string, bool)",
                newMethod: "SampleLib.ReplaceInvocationHelper.Generate(string, bool, int)",
                scope: null,
                CancellationToken.None);

            Assert.IsNotNull(preview, "Preview must be returned for a valid reorder.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken), "Preview token must be populated.");

            var fixtureChange = preview.Changes
                .FirstOrDefault(c => c.FilePath.EndsWith("ReplaceInvocationFixture.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(fixtureChange, "The fixture file must appear in the preview changes.");

            var addedLines = fixtureChange.UnifiedDiff.Split('\n')
                .Where(line => line.StartsWith('\u002B') && !line.StartsWith("\u002B\u002B\u002B"))
                .ToList();
            var addedText = string.Join('\n', addedLines);

            // Positional expressions keep lexical order and bind through target names.
            StringAssert.Contains(
                addedText,
                "ReplaceInvocationHelper.Generate(arg1: a, arg2: b, arg3: c)",
                "Positional expressions must keep lexical order and bind by parameter name.");

            // Literal arguments retain their lexical order.
            StringAssert.Contains(
                addedText,
                "ReplaceInvocationHelper.Generate(arg1: 1, arg2: \"x\", arg3: true)",
                "Literal arguments must bind by target parameter names.");

            // Named arguments retain their original expression evaluation order.
            StringAssert.Contains(
                addedText,
                "ReplaceInvocationHelper.Generate(arg3: true, arg1: 1, arg2: \"x\")",
                "Named arguments must retain original lexical evaluation order.");

            // Negative assertion — no call-site should still reference the old method name.
            Assert.IsFalse(
                addedText.Contains("ReplaceInvocationHelper.Build(", StringComparison.Ordinal),
                "All call-sites must be rewritten to Generate; no Build( invocations should remain in the post-rewrite lines.");

            // CallsiteUpdates: exactly one file, three rewritten invocations.
            Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates should be populated for a non-zero rewrite.");
            Assert.AreEqual(1, preview.CallsiteUpdates.Count, "Only the fixture file should be touched.");
            Assert.AreEqual(3, preview.CallsiteUpdates[0].CallsiteCount, "Three call-sites were rewritten.");
        }
        finally
        {
            WorkspaceManager.Close(scopedWorkspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    /// <summary>
    /// replace-invocation-rewrites-replacement-body: when the replacement method delegates to
    /// the old method (SummarizeV2 => Summarize(...)), the call inside SummarizeV2's own body
    /// must NOT be rewritten — doing so would turn the replacement into infinite recursion.
    /// Only the external caller is rewritten, and the callsite count reflects that.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_DelegatingReplacement_LeavesReplacementBodyUnchanged()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ReplaceInvocationDelegatingFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public static class ReplaceInvocationDelegating
            {
                public static string Summarize(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];

                public static string SummarizeV2(string text, int maxLength)
                {
                    return Summarize(text, maxLength).Trim();
                }
            }

            public static class ReplaceInvocationDelegatingCallers
            {
                public static string Use() => ReplaceInvocationDelegating.Summarize("abcdef", 3);
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var scopedWorkspaceId = loadResult.WorkspaceId;

        try
        {
            var preview = await BulkRefactoringService.PreviewReplaceInvocationAsync(
                scopedWorkspaceId,
                oldMethod: "SampleLib.ReplaceInvocationDelegating.Summarize(string, int)",
                newMethod: "SampleLib.ReplaceInvocationDelegating.SummarizeV2(string, int)",
                scope: null,
                CancellationToken.None);

            var fixtureChange = preview.Changes
                .FirstOrDefault(c => c.FilePath.EndsWith("ReplaceInvocationDelegatingFixture.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(fixtureChange, "The fixture file must appear in the preview changes.");

            var diffLines = fixtureChange.UnifiedDiff.Split('\n');
            var addedText = string.Join('\n', diffLines
                .Where(line => line.StartsWith('+') && !line.StartsWith("+++")));
            var removedText = string.Join('\n', diffLines
                .Where(line => line.StartsWith('-') && !line.StartsWith("---")));

            StringAssert.Contains(
                addedText,
                "ReplaceInvocationDelegating.SummarizeV2(text: \"abcdef\", maxLength: 3)",
                "The external caller must be rewritten to the replacement method.");

            Assert.IsFalse(
                removedText.Contains(".Trim()", StringComparison.Ordinal),
                "The replacement method's own body must not be touched by the rewrite.");
            Assert.IsFalse(
                addedText.Contains("SummarizeV2(text, maxLength)", StringComparison.Ordinal),
                "The delegating call inside SummarizeV2 must not be rewritten into a self-recursive call.");

            Assert.IsNotNull(preview.CallsiteUpdates, "CallsiteUpdates should be populated for a non-zero rewrite.");
            Assert.AreEqual(1, preview.CallsiteUpdates.Count, "Only the fixture file should be touched.");
            Assert.AreEqual(1, preview.CallsiteUpdates[0].CallsiteCount,
                "Only the external call-site is rewritten; the call inside the replacement body is excluded.");
        }
        finally
        {
            WorkspaceManager.Close(scopedWorkspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    /// <summary>
    /// Missing parens → clear ArgumentException. Guard: the FQ parser must reject input that
    /// has no '(' so callers cannot accidentally pass just a method name and get a confusing
    /// downstream "overload match failed" message.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_MissingParens_ThrowsArgument()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicArgumentException>(() =>
                BulkRefactoringService.PreviewReplaceInvocationAsync(
                    loadResult.WorkspaceId,
                    oldMethod: "SampleLib.ReplaceInvocationHelper.Build",
                    newMethod: "SampleLib.ReplaceInvocationHelper.Generate(string, bool, int)",
                    scope: null,
                    CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            TryDeleteDirectory(Path.GetDirectoryName(copiedSolutionPath)!);
        }
    }

    /// <summary>
    /// When the new method declares a parameter name that does not exist on the old method,
    /// the reorder mapping is ambiguous — the service must refuse with InvalidOperationException
    /// rather than silently emit a positional mapping that happens to match by type alone.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_NewParamName_NotInOldMethod_Throws()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var fixturePath = Path.Combine(sampleLibDir, "ReplaceInvocationParamNameFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            namespace SampleLib;

            public static class ReplaceInvocationMismatch
            {
                public static int OldFn(int aa, int bb) => aa - bb;
                public static int NewFn(int cc, int dd) => cc - dd;
            }

            public static class ReplaceInvocationMismatchCallers
            {
                public static int Go() => ReplaceInvocationMismatch.OldFn(1, 2);
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                BulkRefactoringService.PreviewReplaceInvocationAsync(
                    loadResult.WorkspaceId,
                    oldMethod: "SampleLib.ReplaceInvocationMismatch.OldFn(int, int)",
                    newMethod: "SampleLib.ReplaceInvocationMismatch.NewFn(int, int)",
                    scope: null,
                    CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    /// <summary>
    /// scope must be null or "all" — any other value fails fast with ArgumentException so a
    /// stale caller who passes "parameters" (a valid value for bulk_replace_type_preview)
    /// gets a clear error instead of silently expanded scope.
    /// </summary>
    [TestMethod]
    public async Task ReplaceInvocation_InvalidScope_ThrowsArgument()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);

        try
        {
            await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicArgumentException>(() =>
                BulkRefactoringService.PreviewReplaceInvocationAsync(
                    loadResult.WorkspaceId,
                    oldMethod: "SampleLib.ReplaceInvocationHelper.Build(int, string, bool)",
                    newMethod: "SampleLib.ReplaceInvocationHelper.Generate(string, bool, int)",
                    scope: "parameters",
                    CancellationToken.None));
        }
        finally
        {
            WorkspaceManager.Close(loadResult.WorkspaceId);
            TryDeleteDirectory(Path.GetDirectoryName(copiedSolutionPath)!);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
