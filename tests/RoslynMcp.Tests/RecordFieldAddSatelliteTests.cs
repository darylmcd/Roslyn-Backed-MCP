using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Exercises <c>record_field_add_with_satellites_preview</c> on <see cref="SymbolRefactorService"/>.
/// The service is expected to be conservative: propose satellite edits only when ≥2 sibling
/// fields share identical satellite coverage (the "pattern"), and surface a structured empty
/// result with a detection reason when that threshold is not met. This test class pins three
/// scenarios:
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
            PreviewStore,
            RefactoringService,
            EditService,
            restructureService,
            compositeStore,
            DiRegistrationService);
    }
}
