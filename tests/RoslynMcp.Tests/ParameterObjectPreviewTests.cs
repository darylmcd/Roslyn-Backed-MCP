using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for the v1 <c>parameter_object_preview</c> tool. The seven cases mirror the
/// regression set named in <c>ai_docs/items/parameter-object-preview-design.md</c> §(f):
/// (1) positional grouping, (2) named + mixed call sites, (3) default-value refusal,
/// (4) ref/out refusal, (5) cross-project success, (6) cross-project missing-reference
/// refusal, (7) end-to-end redemption through <c>apply_with_verify</c>.
/// </summary>
[TestClass]
public sealed class ParameterObjectPreviewTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task PositionalGrouping_SingleProject_RewritesBothDeclarationAndCallsite()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POPosFixture
            {
                public int Sum(int a, int b, int c) => a + b + c;

                public int Caller() => Sum(1, 2, 3);
            }
            """,
            "POPosFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16), // 'Sum'
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            var fixtureDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("POPosFixture.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(fixtureDiff);
            StringAssert.Contains(fixtureDiff, "SumArgs sumArgs", "declaration should take the new record param");
            StringAssert.Contains(
                fixtureDiff,
                "sumArgs.A + sumArgs.B + sumArgs.C",
                "expression-bodied parameter references should flow through the new record param");
            StringAssert.Contains(fixtureDiff, "new SumArgs(1, 2, 3)", "callsite should wrap grouped args in new SumArgs(...)");

            var addedDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("SumArgs.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(addedDiff, "preview must include the new DTO file");
            StringAssert.Contains(addedDiff, "public sealed record SumArgs(int A, int B, int C)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task BlockBody_RewritesOnlyGroupedParameterSymbols()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POBlockBodyFixture
            {
                private readonly int a = 10;

                public int Sum(int a, int b, int c)
                {
                    return a + b + c + this.a;
                }

                public int Caller() => Sum(1, 2, 3);
            }
            """,
            "POBlockBodyFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 7, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            var diff = preview.Changes.First(c =>
                c.FilePath.EndsWith("POBlockBodyFixture.cs", StringComparison.OrdinalIgnoreCase)).UnifiedDiff;
            StringAssert.Contains(diff, "sumArgs.A + sumArgs.B + sumArgs.C + this.a");
            Assert.IsFalse(
                diff.Contains("this.sumArgs.A", StringComparison.Ordinal),
                "The same-named field must remain bound to the field symbol.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task WrittenOrAliasedGroupedParameters_RefusePreviewWithActionableUses()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POWrittenFixture
            {
                private static void Touch(ref int value) { }
                private static void Fill(out int value) => value = 1;
                private static void Read(in int value) { }

                public unsafe int Compute(int assigned, int incremented, int byRef, int byOut, int byIn, int aliased, int addressed, int typedReference, int refReceiver)
                {
                    assigned = 1;
                    incremented++;
                    Touch(ref byRef);
                    Fill(out byOut);
                    Read(in byIn);
                    ref int alias = ref aliased;
                    int* pointer = &addressed;
                    TypedReference reference = __makeref(typedReference);
                    refReceiver.Increment();
                    return assigned + incremented + byRef + byOut + byIn + alias + *pointer + __refvalue(reference, int) + refReceiver;
                }
            }

            public static class POWrittenExtensions
            {
                public static void Increment(this ref int value) => value++;
            }
            """,
            "POWrittenFixture.cs",
            allowUnsafe: true);

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 9, column: 23),
                    new ParameterObjectPreviewRequest(
                        ["assigned", "incremented", "byRef", "byOut", "byIn", "aliased", "addressed", "typedReference", "refReceiver"],
                        "ComputeArgs"),
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "'assigned'");
            StringAssert.Contains(ex.Message, "(assignment)");
            StringAssert.Contains(ex.Message, "'incremented'");
            StringAssert.Contains(ex.Message, "(increment/decrement)");
            StringAssert.Contains(ex.Message, "'byRef'");
            StringAssert.Contains(ex.Message, "(ref argument)");
            StringAssert.Contains(ex.Message, "'byOut'");
            StringAssert.Contains(ex.Message, "(out argument)");
            StringAssert.Contains(ex.Message, "'byIn'");
            StringAssert.Contains(ex.Message, "(in argument)");
            StringAssert.Contains(ex.Message, "'aliased'");
            StringAssert.Contains(ex.Message, "(ref expression)");
            StringAssert.Contains(ex.Message, "'addressed'");
            StringAssert.Contains(ex.Message, "(address-of expression)");
            StringAssert.Contains(ex.Message, "'typedReference'");
            StringAssert.Contains(ex.Message, "(typed-reference alias)");
            StringAssert.Contains(ex.Message, "'refReceiver'");
            StringAssert.Contains(ex.Message, "(ref extension receiver)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task NewParameterName_InvalidRetainedOrLocalCollision_RefusesPreview()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PONameFixture
            {
                public int Retained(int a, int b, int sumArgs) => a + b + sumArgs;

                public int Local(int a, int b)
                {
                    int sumArgs = 3;
                    return a + b + sumArgs;
                }
            }
            """,
            "PONameFixture.cs");

        try
        {
            var invalid = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b"], "Args", ParameterName: "bad-name"),
                    CancellationToken.None));
            StringAssert.Contains(invalid.Message, "not a valid C# identifier");

            var invalidDefault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b"], "Class"),
                    CancellationToken.None));
            StringAssert.Contains(invalidDefault.Message, "reserved C# keyword");

            var retained = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b"], "SumArgs"),
                    CancellationToken.None));
            StringAssert.Contains(retained.Message, "collides with retained parameter 'sumArgs'");

            var local = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 7, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b"], "SumArgs"),
                    CancellationToken.None));
            StringAssert.Contains(local.Message, "collides with a declaration inside the target method");
            StringAssert.Contains(local.Message, "Local 'sumArgs'");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    [DataRow(8, "SumArgs", "Field 'sumArgs'")]
    [DataRow(9, "SumArgsMethod", "Method 'sumArgsMethod'")]
    public async Task NewParameterName_UnqualifiedMemberReferenceCapture_RefusesPreview(
        int methodLine,
        string newTypeName,
        string expectedMember)
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POMemberCaptureFixture
            {
                private readonly int sumArgs = 10;
                private int sumArgsMethod() => 20;

                public int Field(int a, int b) => a + b + sumArgs;
                public int Method(int a, int b) => a + b + sumArgsMethod();
            }
            """,
            "POMemberCaptureFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, methodLine, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b"], newTypeName),
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "would capture an existing unqualified member reference");
            StringAssert.Contains(ex.Message, expectedMember);
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    [DataRow("a", "A", "CombineArgs", "duplicate positional-record members", "'A' from ['a', 'A']")]
    [DataRow("equals", "other", "CombineArgs", "reserved, synthesized, or inherited", "source parameter 'equals' generates reserved member 'Equals'")]
    [DataRow("deconstruct", "other", "CombineArgs", "reserved, synthesized, or inherited", "source parameter 'deconstruct' generates reserved member 'Deconstruct'")]
    [DataRow("clone", "other", "CombineArgs", "reserved, synthesized, or inherited", "source parameter 'clone' generates reserved member 'Clone'")]
    [DataRow("collision", "other", "Collision", "same name as its enclosing record type", "Source parameter 'collision' generates member 'Collision'")]
    public async Task GeneratedRecordMemberCollision_RefusesPreview(
        string firstParameter,
        string secondParameter,
        string newTypeName,
        string expectedCategory,
        string expectedDetail)
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            $$"""
            namespace SampleLib;

            public class POMemberNameFixture
            {
                public int Combine(int {{firstParameter}}, int {{secondParameter}}) => {{firstParameter}} + {{secondParameter}};
            }
            """,
            "POMemberNameFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                    new ParameterObjectPreviewRequest([firstParameter, secondParameter], newTypeName),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, expectedCategory);
            StringAssert.Contains(ex.Message, expectedDetail);
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task FinalizeGeneratedMember_IsLegalAndAppliesCleanly()
    {
        var (workspaceId, fixturePath, fixtureDir) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POLegalMemberFixture
            {
                public int Combine(int finalize, int other) => finalize + other;

                public int Caller() => Combine(1, 2);
            }
            """,
            "POLegalMemberFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["finalize", "other"], "FinalizeArgs"),
                CancellationToken.None);

            var workflowService = new ApplyUndoWorkflowService(
                RefactoringService,
                CompileCheckService,
                UndoService,
                PreviewStore);
            var applyJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate,
                workflowService,
                PreviewStore,
                preview.PreviewToken,
                rollbackOnError: true,
                ct: CancellationToken.None);
            using var apply = JsonDocument.Parse(applyJson);
            Assert.AreEqual("applied", apply.RootElement.GetProperty("status").GetString(), applyJson);
            Assert.AreEqual(
                apply.RootElement.GetProperty("preErrorCount").GetInt32(),
                apply.RootElement.GetProperty("postErrorCount").GetInt32(),
                applyJson);

            var dtoText = await File.ReadAllTextAsync(Path.Combine(fixtureDir, "FinalizeArgs.cs"));
            StringAssert.Contains(dtoText, "public sealed record FinalizeArgs(int Finalize, int Other);");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    [DataRow(false, "=>")]
    [DataRow(true, "return")]
    public async Task NameofGroupedParameter_PreservesStringAndSameDocumentNestedCallerOnApply(
        bool useBlockBody,
        string expectedBodyPrefix)
    {
        var methodDeclaration = useBlockBody
            ? """
                public string Describe(int a, int b)
                {
                    return nameof(a) + ":" + (a + b);
                }
              """
            : "public string Describe(int a, int b) => nameof(a) + \":\" + (a + b);";
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            $$"""
            namespace SampleLib;

            public class PONameofFixture
            {
                private static int Identity(int value) => value;

            {{methodDeclaration}}

                public string Caller() => Describe(Identity(1), Identity(2));
            }
            """,
            "PONameofFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 7, column: 19),
                new ParameterObjectPreviewRequest(["a", "b"], "DescribeArgs"),
                CancellationToken.None);

            var fixtureDiff = preview.Changes.First(change =>
                change.FilePath.EndsWith("PONameofFixture.cs", StringComparison.OrdinalIgnoreCase)).UnifiedDiff;
            StringAssert.Contains(
                fixtureDiff,
                $"{expectedBodyPrefix} \"a\" + \":\" + (describeArgs.A + describeArgs.B)");
            StringAssert.Contains(
                fixtureDiff,
                "Describe(new DescribeArgs(Identity(1), Identity(2)))",
                "The same-document caller after nameof and its nested invocation arguments must remain correctly targeted.");

            var workflowService = new ApplyUndoWorkflowService(
                RefactoringService,
                CompileCheckService,
                UndoService,
                PreviewStore);
            var applyJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate,
                workflowService,
                PreviewStore,
                preview.PreviewToken,
                rollbackOnError: true,
                ct: CancellationToken.None);
            using var apply = JsonDocument.Parse(applyJson);
            Assert.AreEqual("applied", apply.RootElement.GetProperty("status").GetString(), applyJson);
            Assert.AreEqual(
                apply.RootElement.GetProperty("preErrorCount").GetInt32(),
                apply.RootElement.GetProperty("postErrorCount").GetInt32(),
                applyJson);

            var appliedText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(
                appliedText,
                $"{expectedBodyPrefix} \"a\" + \":\" + (describeArgs.A + describeArgs.B)");
            StringAssert.Contains(appliedText, "Describe(new DescribeArgs(Identity(1), Identity(2)))");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task NamedAndMixedCallSites_RewriteToPositionalDtoConstructor()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PONamedFixture
            {
                public int Sum(int a, int b, int c) => a + b + c;

                public int CallNamed() => Sum(c: 3, a: 1, b: 2);
                public int CallMixed() => Sum(1, c: 3, b: 2);
            }
            """,
            "PONamedFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            var diff = preview.Changes.First(c => c.FilePath.EndsWith("PONamedFixture.cs", StringComparison.OrdinalIgnoreCase)).UnifiedDiff;
            // Both call sites must collapse to the same positional `new SumArgs(1, 2, 3)`.
            var occurrences = CountOccurrences(diff, "new SumArgs(1, 2, 3)");
            Assert.AreEqual(2, occurrences, $"Expected both call sites rewritten to positional. Diff:\n{diff}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task DefaultValueOmissionAtCallSite_RefusesPreview()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PODefaultFixture
            {
                public int Sum(int a, int b = 0, int c = 0) => a + b + c;

                public int Caller() => Sum(a: 1, c: 3); // omits 'b'
            }
            """,
            "PODefaultFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, "omits 'b'");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RefOutParameter_RefusesEntirePreview()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class PORefFixture
            {
                public void Compute(int a, ref int b) { b = a; }

                public void Caller() { int x = 0; Compute(1, ref x); }
            }
            """,
            "PORefFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line: 5, column: 17),
                    new ParameterObjectPreviewRequest(["a", "b"], "ComputeArgs"),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, "by-ref kind");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task CrossProject_RewritesCallSiteWhenReferenceExists()
    {
        // SampleApp already references SampleLib in the SampleSolution fixture, so a method
        // declared in SampleLib with a SampleApp call site exercises the reference-present path.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var libDir = Path.Combine(solutionDir, "SampleLib");
        var appDir = Path.Combine(solutionDir, "SampleApp");

        var libFile = Path.Combine(libDir, "POCrossLib.cs");
        await File.WriteAllTextAsync(libFile,
            """
            namespace SampleLib;

            public class POCrossLib
            {
                public int Sum(int a, int b, int c) => a + b + c;
            }
            """);
        var appFile = Path.Combine(appDir, "POCrossApp.cs");
        await File.WriteAllTextAsync(appFile,
            """
            using SampleLib;

            namespace SampleApp;

            public static class POCrossApp
            {
                public static int Run() => new POCrossLib().Sum(1, 2, 3);
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;
        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(libFile, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            Assert.IsTrue(preview.Changes.Any(c => c.FilePath.EndsWith("POCrossLib.cs", StringComparison.OrdinalIgnoreCase)));
            var appDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("POCrossApp.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(appDiff, "cross-project caller must be rewritten");
            StringAssert.Contains(appDiff, "new SumArgs(1, 2, 3)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task CrossProject_RefusesWhenCallerProjectMissingReference()
    {
        // Place the method in SampleApp (which is NOT referenced by SampleLib.Tests) and
        // request the DTO to land in SampleApp; then create a caller in SampleLib.Tests that
        // can compile against SampleApp's type *only via* a fixture-lib reference. To keep the
        // setup simple, we instead place the method in SampleLib.Tests (which references both)
        // and request the DTO to live in SampleApp — SampleLib.Tests references SampleApp? Let's
        // verify by inverse: method in SampleLib, dtoProjectName=SampleApp. SampleLib does NOT
        // reference SampleApp, so the SampleLib declaration's project is missing the reference.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var libDir = Path.Combine(solutionDir, "SampleLib");

        var libFile = Path.Combine(libDir, "POCrossMissingLib.cs");
        await File.WriteAllTextAsync(libFile,
            """
            namespace SampleLib;

            public class POCrossMissingLib
            {
                public int Sum(int a, int b, int c) => a + b + c;
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(libFile, line: 5, column: 16),
                    new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs", DtoProjectName: "SampleApp"),
                    CancellationToken.None));
            StringAssert.Contains(ex.Message, "SampleLib -> SampleApp");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task ApplyWithVerify_RedeemsPreviewAndWritesChangesToDisk()
    {
        var (workspaceId, fixturePath, fixtureDir) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public class POApplyFixture
            {
                public int Sum(int a, int b, int c) => a + b + c;

                public int Caller() => Sum(1, 2, 3);
            }
            """,
            "POApplyFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 16),
                new ParameterObjectPreviewRequest(["a", "b", "c"], "SumArgs"),
                CancellationToken.None);

            var workflowService = new ApplyUndoWorkflowService(
                RefactoringService,
                CompileCheckService,
                UndoService,
                PreviewStore);
            var applyJson = await ApplyWithVerifyTool.ApplyWithVerify(
                WorkspaceExecutionGate,
                workflowService,
                PreviewStore,
                preview.PreviewToken,
                rollbackOnError: true,
                ct: CancellationToken.None);
            using var apply = JsonDocument.Parse(applyJson);
            Assert.AreEqual(
                "applied",
                apply.RootElement.GetProperty("status").GetString(),
                $"apply_with_verify must redeem a parameter_object_preview token. Response: {applyJson}");
            Assert.AreEqual(
                apply.RootElement.GetProperty("preErrorCount").GetInt32(),
                apply.RootElement.GetProperty("postErrorCount").GetInt32(),
                $"Applying the parameter-object preview must introduce no diagnostics. Response: {applyJson}");

            var dtoPath = Path.Combine(fixtureDir, "SumArgs.cs");
            Assert.IsTrue(File.Exists(dtoPath), "DTO file must be written to disk");
            var dtoText = await File.ReadAllTextAsync(dtoPath);
            StringAssert.Contains(dtoText, "public sealed record SumArgs(int A, int B, int C);");

            var fixtureText = await File.ReadAllTextAsync(fixturePath);
            StringAssert.Contains(fixtureText, "Sum(SumArgs sumArgs)");
            StringAssert.Contains(fixtureText, "sumArgs.A + sumArgs.B + sumArgs.C");
            StringAssert.Contains(fixtureText, "new SumArgs(1, 2, 3)");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    public static IEnumerable<object[]> UnsupportedTargetContractCases =>
    [
        [
            "constructor",
            """
            namespace SampleLib;

            public class POCtorFixture
            {
                public POCtorFixture(int a, int b) { }

                public static POCtorFixture Make() => new POCtorFixture(1, 2);
            }
            """,
            5, 12, new[] { "a", "b" }, "method kind 'Constructor'",
        ],
        [
            "user-defined operator",
            """
            namespace SampleLib;

            public struct POOpFixture
            {
                public static POOpFixture operator +(POOpFixture left, POOpFixture right) => left;
            }
            """,
            5, 40, new[] { "left", "right" }, "method kind 'UserDefinedOperator'",
        ],
        [
            "property accessor",
            """
            namespace SampleLib;

            public class POIndexerFixture
            {
                public int this[int a, int b] { set { } }
            }
            """,
            5, 37, new[] { "a", "b" }, "method kind 'PropertySet'",
        ],
        [
            "override",
            """
            namespace SampleLib;

            public class POBaseFixture
            {
                public virtual int Sum(int a, int b) => a + b;
            }

            public class PODerivedFixture : POBaseFixture
            {
                public override int Sum(int a, int b) => a + b;
            }
            """,
            10, 25, new[] { "a", "b" }, "is an 'override'",
        ],
        [
            "virtual dispatch root",
            """
            namespace SampleLib;

            public class POVirtualFixture
            {
                public virtual int Sum(int a, int b) => a + b;
            }
            """,
            5, 24, new[] { "a", "b" }, "'virtual' dispatch root",
        ],
        [
            "abstract dispatch root",
            """
            namespace SampleLib;

            public abstract class POAbstractFixture
            {
                public abstract int Sum(int a, int b);
            }
            """,
            5, 25, new[] { "a", "b" }, "'abstract' dispatch root",
        ],
        [
            "implicit interface implementation",
            """
            namespace SampleLib;

            public interface IPOContractFixture
            {
                int Sum(int a, int b);
            }

            public class POImplicitImplFixture : IPOContractFixture
            {
                public int Sum(int a, int b) => a + b;
            }
            """,
            10, 16, new[] { "a", "b" }, "implements an interface member",
        ],
        [
            "explicit interface implementation",
            """
            namespace SampleLib;

            public interface IPOExplicitContractFixture
            {
                int Sum(int a, int b);
            }

            public class POExplicitImplFixture : IPOExplicitContractFixture
            {
                int IPOExplicitContractFixture.Sum(int a, int b) => a + b;
            }
            """,
            10, 36, new[] { "a", "b" }, "explicitly implements an interface member",
        ],
        [
            "extern/PInvoke declaration",
            """
            namespace SampleLib;

            public static class PONativeFixture
            {
                [System.Runtime.InteropServices.DllImport("kernel32.dll")]
                public static extern int PONativeSum(int a, int b);
            }
            """,
            6, 30, new[] { "a", "b" }, "extern/PInvoke declaration",
        ],
        [
            "partial definition/implementation pair",
            """
            namespace SampleLib;

            public partial class POPartialFixture
            {
                public partial int Sum(int a, int b);
            }

            public partial class POPartialFixture
            {
                public partial int Sum(int x, int y) => x + y;
            }
            """,
            10, 24, new[] { "x", "y" }, "partial method",
        ],
    ];

    /// <summary>
    /// parameter-object-target-method-contract-validation: every non-ordinary target kind
    /// and non-atomically-rewritable dispatch contract must refuse BEFORE any preview is
    /// constructed — previously these produced a redeemable token whose rewrite broke
    /// call sites, override chains, or paired partial declarations.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(UnsupportedTargetContractCases))]
    public async Task UnsupportedTargetMethodContract_RefusesPreview(
        string caseName,
        string source,
        int line,
        int column,
        string[] parameterNames,
        string expectedFragment)
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            source,
            "POContractFixture.cs");

        try
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ParameterObjectService.PreviewParameterObjectAsync(
                    workspaceId,
                    SymbolLocator.BySource(fixturePath, line, column),
                    new ParameterObjectPreviewRequest(parameterNames, "GroupedArgs"),
                    CancellationToken.None),
                $"case '{caseName}' must refuse the preview");
            StringAssert.Contains(ex.Message, expectedFragment, $"case '{caseName}'");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// Guard against over-refusal: a plain (non-virtual, non-interface, non-partial)
    /// extension method's non-receiver parameters must remain eligible for grouping.
    /// </summary>
    [TestMethod]
    public async Task PlainExtensionMethod_NonReceiverParameters_StillGroup()
    {
        var (workspaceId, fixturePath, _) = await SetupSingleProjectFixtureAsync(
            """
            namespace SampleLib;

            public static class POExtensionGroupFixture
            {
                public static int Sum(this string tag, int a, int b) => tag.Length + a + b;

                public static int Caller() => POExtensionGroupFixture.Sum("x", 1, 2);
            }
            """,
            "POExtensionGroupFixture.cs");

        try
        {
            var preview = await ParameterObjectService.PreviewParameterObjectAsync(
                workspaceId,
                SymbolLocator.BySource(fixturePath, line: 5, column: 23),
                new ParameterObjectPreviewRequest(["a", "b"], "SumArgs"),
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken, "plain extension methods must stay eligible");
            var fixtureDiff = preview.Changes.FirstOrDefault(c => c.FilePath.EndsWith("POExtensionGroupFixture.cs", StringComparison.OrdinalIgnoreCase))?.UnifiedDiff;
            Assert.IsNotNull(fixtureDiff);
            StringAssert.Contains(fixtureDiff, "SumArgs sumArgs", "declaration should take the new record param");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    private static async Task<(string WorkspaceId, string FixturePath, string FixtureDir)> SetupSingleProjectFixtureAsync(
        string content,
        string fileName,
        bool allowUnsafe = false)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var libDir = Path.Combine(solutionDir, "SampleLib");
        if (allowUnsafe)
        {
            var projectPath = Path.Combine(libDir, "SampleLib.csproj");
            var projectText = await File.ReadAllTextAsync(projectPath);
            projectText = projectText.Replace(
                "    <Nullable>enable</Nullable>",
                "    <Nullable>enable</Nullable>\n    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(projectPath, projectText);
        }
        var fixturePath = Path.Combine(libDir, fileName);
        await File.WriteAllTextAsync(fixturePath, content);
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (loadResult.WorkspaceId, fixturePath, libDir);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
