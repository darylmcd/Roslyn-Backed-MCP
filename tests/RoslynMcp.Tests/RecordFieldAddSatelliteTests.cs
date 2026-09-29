using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Exercises <c>record_field_add_with_satellites_preview</c> on <see cref="SymbolRefactorService"/>.
/// The service is expected to be conservative: propose satellite edits only when ≥2 sibling
/// fields share identical satellite coverage (the "pattern"), and surface a structured empty
/// result with a detection reason when that threshold is not met. The baseline cases are:
///
/// <list type="number">
///   <item><description>
///     <c>Infers_Pattern_And_Proposes_Edits_For_Satellite_Sites</c> — target type with two
///     sibling fields (<c>A</c>, <c>B</c>) that each participate in
///     <c>Snapshot.Field</c> + <c>Clone</c> + <c>With</c> + <c>ToJson</c> patterns. Adding
///     <c>C</c> must propose matching edits in all four satellites.
///   </description></item>
///   <item><description>
///     <c>Returns_Empty_Preview_With_Reason_When_Only_One_Sibling_Field</c> — target type with
///     exactly one existing field; no pattern can be inferred (there's no sibling to compare
///     against). Detection reason must explain the single-field case.
///   </description></item>
///   <item><description>
///     <c>Returns_Empty_Preview_When_Sibling_Fields_Have_Divergent_Coverage</c> — target type
///     with two existing fields whose satellite sets do not agree (A has a With but no Clone;
///     B has a Clone but no With). No ≥2-field consensus exists; the detection reason must
///     say so.
///   </description></item>
/// </list>
/// </summary>
[TestClass]
public sealed class RecordFieldAddSatelliteTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OneLineClone_AppliedSatellitePreviewCompiles(bool trailingComma)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "OneLineSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            $"namespace SampleLib.Satellites; public class Metrics {{ public int A {{ get; set; }} public int B {{ get; set; }} public int C {{ get; set; }} public Metrics Clone() => new() {{ A = this.A, B = this.B{(trailingComma ? "," : "")} }}; }}\n");
        await workspace.LoadAsync(CancellationToken.None);

        var store = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(store);
        var preview = await service.PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.Metrics", "C", "int", CancellationToken.None);
        Assert.IsTrue(preview.InferredPattern.Contains("CloneMethodBody"));
        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await AssertSampleLibCompilesAsync(workspace);
        StringAssert.Contains(await File.ReadAllTextAsync(fixturePath), "C = this.C");
    }

    [TestMethod]
    public async Task OneLineSatelliteSites_AppliedPreviewUpdatesTupleResetWithAndCompiles()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "AllSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            using System.Text;
            namespace SampleLib.Satellites;
            public class Metrics
            {
                public int A { get; set; }
                public int B { get; set; }
                public int C { get; set; }
                public Metrics Clone() => new() { A = this.A, B = this.B };
                public (int A, int B) Snapshot() => (A, B);
                public void Reset() { A = 0; B = 0; }
                public Metrics WithA(int value) { A = value; return this; }
                public Metrics WithB(int value) { B = value; return this; }
                public void IncrementA() => A++;
                public void IncrementB() { B++; }
                public string ToJson() { var sb = new StringBuilder(); sb.Append("{").Append(A); sb.Append("{").Append(B); return sb.ToString(); }
            }
            public class MetricsSnapshot { public int A { get; init; } public int B { get; init; } }
            """);
        await workspace.LoadAsync(CancellationToken.None);

        var store = new CompositePreviewStore();
        var originalContent = await File.ReadAllTextAsync(fixturePath);
        var preview = await CreateSymbolRefactorService(store).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.Metrics", "C", "int", CancellationToken.None);
        foreach (var kind in new[] { "CloneMethodBody", "Snapshot.Tuple", "ResetMethodBody",
                     "WithMethod.Assignment", "IncrementMethod", "ToJson.Case", "SnapshotType.Field" })
        {
            Assert.IsTrue(preview.InferredPattern.Contains(kind), $"Missing inferred site {kind}.");
            Assert.IsTrue(preview.ProposedEdits.Any(edit => edit.SiteKind == kind), $"Missing edit for {kind}.");
        }
        Assert.IsTrue(preview.ProposedEdits.GroupBy(edit => edit.Line).Any(group => group.Count() > 1),
            "The fixture must exercise multiple original-offset edits on one line.");
        var originalText = Microsoft.CodeAnalysis.Text.SourceText.From(originalContent);
        var expectedContent = originalContent;
        foreach (var edit in preview.ProposedEdits.OrderByDescending(edit =>
                     originalText.Lines[edit.Line - 1].Start + edit.Column - 1))
        {
            var offset = originalText.Lines[edit.Line - 1].Start + edit.Column - 1;
            expectedContent = expectedContent.Insert(offset, edit.NewText);
        }
        Assert.AreEqual(expectedContent, store.Retrieve(preview.PreviewToken!)!.Value.Mutations.Single().UpdatedContent,
            "ProposedEdits must reproduce the exact composite mutation using original offsets.");

        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        var content = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(content, "C = this.C");
        StringAssert.Contains(content, "(int A, int B, int C)");
        StringAssert.Contains(content, "(A, B, C)");
        StringAssert.Contains(content, "C = 0;");
        StringAssert.Contains(content, "WithC(int value)");
        StringAssert.Contains(content, "IncrementC");
        StringAssert.Contains(content, "Append(C)");
        await AssertSampleLibCompilesAsync(workspace);
    }

    [TestMethod]
    public async Task CopyPerFieldMethods_AppliedPreviewAddsCopyMethod()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "CopySatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class CopyMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public CopyMetrics CopyA() => new() { A = this.A }; public CopyMetrics CopyB() => new() { B = this.B }; }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var store = new CompositePreviewStore();
        var preview = await CreateSymbolRefactorService(store).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.CopyMetrics", "C", "int", CancellationToken.None);
        Assert.IsTrue(preview.InferredPattern.Contains("CloneMethodBody"));
        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        StringAssert.Contains(await File.ReadAllTextAsync(fixturePath), "CopyC()");
        await AssertSampleLibCompilesAsync(workspace);
    }

    [TestMethod]
    public async Task CloneAndCopyMethods_AppliedPreviewUpdatesBothSites()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "CloneAndCopySatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class CloneAndCopyMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public CloneAndCopyMetrics Clone() => new() { A = this.A, B = this.B }; public CloneAndCopyMetrics CopyA() => new() { A = this.A }; public CloneAndCopyMetrics CopyB() => new() { B = this.B }; }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var store = new CompositePreviewStore();
        var preview = await CreateSymbolRefactorService(store).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.CloneAndCopyMetrics", "C", "int", CancellationToken.None);
        Assert.AreEqual(2, preview.ProposedEdits.Count(edit => edit.SiteKind == "CloneMethodBody"));
        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        var content = await File.ReadAllTextAsync(fixturePath);
        StringAssert.Contains(content, "C = this.C");
        StringAssert.Contains(content, "CopyC()");
        await AssertSampleLibCompilesAsync(workspace);
    }

    [TestMethod]
    public async Task MultipleTupleResetAndJsonMethods_AppliedPreviewUpdatesEverySite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "MultiMethodSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            """
            using System.Text;
            namespace SampleLib.Satellites;
            public class MultiMethodMetrics
            {
                public int A { get; set; }
                public int B { get; set; }
                public int C { get; set; }
                public (int A, int B) Snapshot() => (A, B);
                public (int A, int B) Snapshot(bool include) => (A, B);
                public void Reset() { A = 0; B = 0; }
                public void Reset(bool hard) { A = 0; B = 0; }
                public string ToJson() { var sb = new StringBuilder(); sb.Append(A); sb.Append(B); return sb.ToString(); }
                public string Serialize() { var sb = new StringBuilder(); sb.Append(A); sb.Append(B); return sb.ToString(); }
            }
            """);
        await workspace.LoadAsync(CancellationToken.None);

        var store = new CompositePreviewStore();
        var preview = await CreateSymbolRefactorService(store).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.MultiMethodMetrics", "C", "int", CancellationToken.None);
        Assert.AreEqual(4, preview.ProposedEdits.Count(edit => edit.SiteKind == "Snapshot.Tuple"));
        Assert.AreEqual(2, preview.ProposedEdits.Count(edit => edit.SiteKind == "ResetMethodBody"));
        Assert.AreEqual(2, preview.ProposedEdits.Count(edit => edit.SiteKind == "ToJson.Case"));
        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await AssertSampleLibCompilesAsync(workspace);
    }

    [TestMethod]
    public async Task SnapshotReceiver_AppliedPreviewUsesSiblingReceiver()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "ReceiverSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class ReceiverMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public (int A, int B) Snapshot(ReceiverMetrics source) => (source.A, source.B); }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var store = new CompositePreviewStore();
        var preview = await CreateSymbolRefactorService(store).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.ReceiverMetrics", "C", "int", CancellationToken.None);
        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, store)
            .ApplyCompositeAsync(preview.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        StringAssert.Contains(await File.ReadAllTextAsync(fixturePath), "(source.A, source.B, source.C)");
        await AssertSampleLibCompilesAsync(workspace);
    }

    [TestMethod]
    public async Task WithMethodUnrelatedSameNameIdentifier_RefusesUnsafeRewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "WithCollisionSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class B { public int Offset => 1; } public class WithCollisionMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public WithCollisionMetrics WithA(int value) { A = value; return this; } public WithCollisionMetrics WithB(int value) { B helper = new B(); this.B = value + helper.Offset; return this; } }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.WithCollisionMetrics", "C", "int", CancellationToken.None);
        Assert.IsTrue(string.IsNullOrWhiteSpace(preview.PreviewToken));
        StringAssert.Contains(preview.PatternDetectionReason, "unsupported");
    }

    [TestMethod]
    public async Task ToJsonUnrelatedSameNameIdentifier_RefusesUnsafeRewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "JsonCollisionSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class B { public int Offset => 1; } public class JsonCollisionMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public string ToJson() { var text = new System.Text.StringBuilder(); text.Append(A); text.Append(new B().Offset).Append(this.B); return text.ToString(); } }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.JsonCollisionMetrics", "C", "int", CancellationToken.None);
        Assert.IsNull(preview.PreviewToken);
        Assert.IsEmpty(preview.ProposedEdits);
        StringAssert.Contains(preview.PatternDetectionReason, "unsupported");
    }

    [TestMethod]
    public async Task CloneUnrelatedSameNameIdentifier_RefusesUnsafeRewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "CloneCollisionSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class B { public int Value => 1; } public class CloneCollisionMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public CloneCollisionMetrics Clone() => new() { A = this.A, B = new B().Value + this.B }; }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.CloneCollisionMetrics", "C", "int", CancellationToken.None);
        Assert.IsNull(preview.PreviewToken);
        Assert.IsEmpty(preview.ProposedEdits);
        StringAssert.Contains(preview.PatternDetectionReason, "unsupported");
    }

    [TestMethod]
    public async Task ToJsonAmbiguousFieldText_RefusesLiteralRewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "JsonLiteralSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class JsonLiteralMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public string ToJson() { var text = new System.Text.StringBuilder(); text.Append(\"A\").Append(A); text.Append(\"B\").Append(B); return text.ToString(); } }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.JsonLiteralMetrics", "C", "int", CancellationToken.None);
        Assert.IsNull(preview.PreviewToken);
        Assert.IsEmpty(preview.ProposedEdits);
        StringAssert.Contains(preview.PatternDetectionReason, "unsupported");
    }

    [TestMethod]
    public async Task CloneUnrelatedReceiver_RefusesMemberRewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "CloneReceiverSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class Helper { public int B => 1; } public class CloneReceiverMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public CloneReceiverMetrics Clone(Helper helper) => new() { A = this.A, B = helper.B }; }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CreateSymbolRefactorService(new CompositePreviewStore()).PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId, "SampleLib.Satellites.CloneReceiverMetrics", "C", "int", CancellationToken.None);
        Assert.IsNull(preview.PreviewToken);
        Assert.IsEmpty(preview.ProposedEdits);
        StringAssert.Contains(preview.PatternDetectionReason, "unsupported");
    }

    [TestMethod]
    public async Task UnsupportedWithShape_ReturnsReasonWithoutPartialPreview()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var fixturePath = workspace.GetPath("SampleLib", "UnsupportedSatelliteFixture.cs");
        await File.WriteAllTextAsync(fixturePath,
            "namespace SampleLib.Satellites; public class UnsupportedMetrics { public int A { get; set; } public int B { get; set; } public int C { get; set; } public UnsupportedMetrics With(int a, int b) { A = a; B = b; return this; } }\n");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CreateSymbolRefactorService(new CompositePreviewStore())
            .PreviewRecordFieldAddWithSatellitesAsync(workspace.WorkspaceId,
                "SampleLib.Satellites.UnsupportedMetrics", "C", "int", CancellationToken.None);
        Assert.IsNull(preview.PreviewToken);
        Assert.IsEmpty(preview.ProposedEdits);
        StringAssert.Contains(preview.PatternDetectionReason, "unsupported");
        StringAssert.Contains(preview.PatternDetectionReason, "WithMethod.Assignment");
    }

    [TestMethod]
    public async Task Infers_Pattern_And_Proposes_Edits_For_Satellite_Sites()
    {
        // Fixture: `Counters` struct with `A` and `B` integer fields that each appear in:
        //   - Snapshot.Field — a sibling `CountersSnapshot` declares properties of the same names.
        //   - CloneMethodBody — `Clone(Counters source)` assigns each field.
        //   - WithMethod.Assignment — `WithA(...)` / `WithB(...)` methods assign each field.
        //   - ToJson.Case — a `ToJson(StringBuilder sb)` method has one line per field.
        // Both fields have IDENTICAL coverage, so the pattern is inferred and all four kinds
        // appear in the InferredPattern list. Adding `C` must propose one edit per kind.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "CountersFixture.cs");
        await File.WriteAllTextAsync(
            fixturePath,
            """
            using System.Text;

            namespace SampleLib.Counters;

            public class Counters
            {
                public int A { get; set; }
                public int B { get; set; }
                public int C { get; set; }

                public Counters Clone(Counters source)
                {
                    var result = new Counters();
                    result.A = source.A;
                    result.B = source.B;
                    return result;
                }

                public Counters WithA(int value)
                {
                    A = value;
                    return this;
                }

                public Counters WithB(int value)
                {
                    B = value;
                    return this;
                }

                public void ToJson(StringBuilder sb)
                {
                    sb.Append("\"A\":").Append(A).Append(',');
                    sb.Append("\"B\":").Append(B);
                }
            }

            public class CountersSnapshot
            {
                public int A { get; init; }
                public int B { get; init; }
            }
            """,
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        var result = await service.PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId,
            typeMetadataName: "SampleLib.Counters.Counters",
            newFieldName: "C",
            newFieldType: "int",
            CancellationToken.None);

        Assert.AreEqual("SampleLib.Counters.Counters", result.TargetTypeDisplay);
        Assert.AreEqual("C", result.NewField.Name);
        Assert.AreEqual("int", result.NewField.Type);

        Assert.IsTrue(result.InferredPattern.Count >= 4,
            $"Expected ≥4 satellite kinds in pattern, got {result.InferredPattern.Count}: [{string.Join(", ", result.InferredPattern)}]");

        // The four structural kinds must all surface. Exact labels are contractual — callers
        // filter edits by SiteKind so any rename is a breaking change.
        Assert.IsTrue(result.InferredPattern.Contains("SnapshotType.Field"),
            "Mirror type detection must contribute SnapshotType.Field when siblings declare same-named members.");
        Assert.IsTrue(result.InferredPattern.Contains("CloneMethodBody"),
            "Clone(source) assignment detection must contribute CloneMethodBody when siblings' Clone body assigns each field.");
        Assert.IsTrue(result.InferredPattern.Contains("WithMethod.Assignment"),
            "With{Field} methods must contribute WithMethod.Assignment when siblings assign matching field names.");
        Assert.IsTrue(result.InferredPattern.Contains("ToJson.Case"),
            "ToJson-style method must contribute ToJson.Case when the method body references sibling field names.");

        // One edit per structural kind (minimum — the synthesizer uses the last-matching anchor
        // per kind, so duplicate anchors don't inflate the count).
        Assert.IsTrue(result.ProposedEdits.Count >= result.InferredPattern.Count,
            $"Expected at least {result.InferredPattern.Count} edits (one per kind), got {result.ProposedEdits.Count}.");

        // Each edit must name the target file and include NewField.Name in its NewText so the
        // reviewer sees a non-empty rewrite.
        foreach (var edit in result.ProposedEdits)
        {
            Assert.AreEqual(fixturePath, edit.FilePath,
                "All edits must be scoped to the declaring file in this fixture.");
            StringAssert.Contains(edit.NewText, "C",
                $"Edit ({edit.SiteKind}) must splice the new field name 'C'. NewText: {edit.NewText}");
            Assert.IsFalse(string.IsNullOrEmpty(edit.SiteKind),
                "Every edit must be labelled with its SiteKind for filter-by-kind flows.");
        }

        // Preview-token round-trip: the composite store must return the recorded mutation set.
        Assert.IsFalse(string.IsNullOrEmpty(result.PreviewToken),
            "A non-empty pattern must yield a non-null preview token.");
        var retrieved = compositeStore.Retrieve(result.PreviewToken!);
        Assert.IsNotNull(retrieved, "Preview token must be retrievable from the composite store.");
        Assert.AreEqual(1, retrieved.Value.Mutations.Count,
            "All edits should land in the single declaring file for this fixture.");

        var applied = await new CompositeApplyOrchestrator(WorkspaceManager, compositeStore)
            .ApplyCompositeAsync(result.PreviewToken!, CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        StringAssert.Contains(await File.ReadAllTextAsync(fixturePath), "sb.Append(\"\\\"C\\\":\").Append(C)");
        await AssertSampleLibCompilesAsync(workspace);

        // Detection reason is empty when the pattern was inferred.
        Assert.AreEqual(string.Empty, result.PatternDetectionReason,
            "PatternDetectionReason must be empty when InferredPattern is non-empty.");
    }

    [TestMethod]
    public async Task Returns_Empty_Preview_With_Reason_When_Only_One_Sibling_Field()
    {
        // Fixture: target type has a single field. There is no "sibling" to compare satellite
        // coverage against, so the conservative rule (≥2 siblings) prevents any pattern from
        // being declared. The result must be an empty preview with a clear reason.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "SingleFieldFixture.cs");
        await File.WriteAllTextAsync(
            fixturePath,
            """
            namespace SampleLib.SingleField;

            public class SoloCounter
            {
                public int A { get; set; }

                public SoloCounter Clone(SoloCounter source)
                {
                    var result = new SoloCounter();
                    result.A = source.A;
                    return result;
                }
            }

            public class SoloCounterSnapshot
            {
                public int A { get; init; }
            }
            """,
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        var result = await service.PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId,
            typeMetadataName: "SampleLib.SingleField.SoloCounter",
            newFieldName: "B",
            newFieldType: "int",
            CancellationToken.None);

        Assert.AreEqual(0, result.InferredPattern.Count,
            "A single-sibling type cannot establish a ≥2-field pattern — InferredPattern must be empty.");
        Assert.AreEqual(0, result.ProposedEdits.Count,
            "No edits may be proposed when InferredPattern is empty.");
        Assert.IsNull(result.PreviewToken,
            "No preview token should be issued when there are no edits to apply.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PatternDetectionReason),
            "Empty-pattern results must carry a non-empty PatternDetectionReason so the caller knows why.");
        StringAssert.Contains(result.PatternDetectionReason, "1 existing field",
            $"Reason must reference the single-field case. Actual: {result.PatternDetectionReason}");
    }

    [TestMethod]
    public async Task Returns_Empty_Preview_When_Sibling_Fields_Have_Divergent_Coverage()
    {
        // Fixture: target type has two existing fields whose satellite coverage sets DO NOT
        // agree. `A` has a `WithA` method (WithMethod.Assignment) but nothing else. `B` has a
        // `Clone` body assignment (CloneMethodBody) but no With. Neither pattern has ≥2 fields
        // participating, so no kind reaches the threshold — the result is an empty preview.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var fixturePath = workspace.GetPath("SampleLib", "DivergentFixture.cs");
        await File.WriteAllTextAsync(
            fixturePath,
            """
            namespace SampleLib.Divergent;

            public class Divergent
            {
                public int A { get; set; }
                public int B { get; set; }

                public Divergent WithA(int value)
                {
                    A = value;
                    return this;
                }

                public Divergent Clone(Divergent source)
                {
                    var result = new Divergent();
                    result.B = source.B;
                    return result;
                }
            }
            """,
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);

        var result = await service.PreviewRecordFieldAddWithSatellitesAsync(
            workspace.WorkspaceId,
            typeMetadataName: "SampleLib.Divergent.Divergent",
            newFieldName: "C",
            newFieldType: "int",
            CancellationToken.None);

        Assert.AreEqual(0, result.InferredPattern.Count,
            "Divergent coverage must NOT yield any inferred pattern — prefer false-negative over false-positive.");
        Assert.AreEqual(0, result.ProposedEdits.Count,
            "No edits may be proposed when InferredPattern is empty.");
        Assert.IsNull(result.PreviewToken,
            "No preview token should be issued when there are no edits to apply.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PatternDetectionReason),
            "Empty-pattern results must carry a non-empty PatternDetectionReason.");
        StringAssert.Contains(result.PatternDetectionReason, "divergent",
            $"Reason must identify the divergent-coverage case. Actual: {result.PatternDetectionReason}");
    }

    private static SymbolRefactorService CreateSymbolRefactorService(CompositePreviewStore compositeStore)
    {
        var restructureService = new RestructureService(WorkspaceManager, PreviewStore);
        return new SymbolRefactorService(
            WorkspaceManager,
            RefactoringService,
            EditService,
            restructureService,
            compositeStore,
            DiRegistrationService);
    }

    private static async Task AssertSampleLibCompilesAsync(IsolatedWorkspaceScope workspace)
    {
        await workspace.ReloadAsync(CancellationToken.None);
        var project = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects
            .Single(candidate => candidate.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics(CancellationToken.None)
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString()).ToArray();
        Assert.AreEqual(0, errors.Length, string.Join("\n", errors));
    }
}
