namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
public sealed class RecordFieldAdditionImpactTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task PreviewAddition_ClassifiesAllCategoriesOfImpact_ForPositionalRecord()
    {
        // Plan validation fixture: add `bool NewFlag` to `TestRecord(int A, string B)`.
        // Impact output must list every `new TestRecord(x, y)` construction site, every
        // `var (a, b) = testRecord` deconstruction, and flag the property patterns that
        // omit `NewFlag`. We write a self-contained fixture file so the test is fully
        // reproducible and the signal isn't noisy from unrelated sample types.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var libPath = Path.Combine(copiedRoot, "SampleLib", "TestRecordFixture.cs");
            var testPath = Path.Combine(copiedRoot, "SampleLib.Tests", "TestRecordConsumerTests.cs");
            var consumerPath = Path.Combine(copiedRoot, "SampleApp", "TestRecordConsumer.cs");

            await File.WriteAllTextAsync(libPath, """
namespace SampleLib.Records;

public record TestRecord(int A, string B);
""", CancellationToken.None);

            // Consumer file in SampleApp — production construction + with + property-pattern sites.
            await File.WriteAllTextAsync(consumerPath, """
using SampleLib.Records;

namespace SampleApp.RecordImpactConsumers;

public static class TestRecordConsumer
{
    public static TestRecord MakeOne() => new TestRecord(1, "first");

    public static TestRecord CopyWith(TestRecord original) => original with { A = original.A + 1 };

    public static string Describe(TestRecord r) => r switch
    {
        { A: 0, B: "" } => "empty",
        { A: var a, B: var b } => $"{a}/{b}",
    };

    public static string Deconstruct(TestRecord r)
    {
        var (a, b) = r;
        return a + "/" + b;
    }
}
""", CancellationToken.None);

            // Test file — production construction in test fixtures. The service should route this
            // into TestFilesConstructing because the project is an MSBuild test project.
            await File.WriteAllTextAsync(testPath, """
using SampleLib.Records;

namespace SampleLib.Tests.RecordImpactFixtures;

public static class TestRecordConsumerTests
{
    public static TestRecord Fixture() => new TestRecord(42, "fixture");
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var result = await RecordFieldAdditionService.PreviewAdditionAsync(
                workspaceId,
                recordMetadataName: "SampleLib.Records.TestRecord",
                newFieldName: "NewFlag",
                newFieldType: "bool",
                defaultValueExpression: "false",
                CancellationToken.None);

            // Shape assertions.
            Assert.AreEqual("SampleLib.Records.TestRecord", result.TargetRecordDisplay);
            Assert.IsTrue(result.IsPositionalRecord, "TestRecord has a primary ctor so it must be positional.");
            Assert.AreEqual(2, result.ExistingPositionalParameters.Count);
            Assert.AreEqual("A", result.ExistingPositionalParameters[0].Name);
            Assert.AreEqual("int", result.ExistingPositionalParameters[0].Type);
            Assert.AreEqual("B", result.ExistingPositionalParameters[1].Name);
            Assert.AreEqual("string", result.ExistingPositionalParameters[1].Type);

            // Construction sites: the production consumer + the test file both construct with 2 args.
            Assert.IsTrue(result.PositionalConstructionSites.Count >= 2,
                $"Expected at least 2 construction sites (consumer + test fixture), got {result.PositionalConstructionSites.Count}.");
            var prodConstruction = result.PositionalConstructionSites
                .FirstOrDefault(c => c.Location.FilePath.EndsWith("TestRecordConsumer.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(prodConstruction, "Production construction site (TestRecordConsumer.cs) must be present.");
            Assert.AreEqual("(1, \"first\")", prodConstruction.OriginalArgumentList);
            Assert.AreEqual("(1, \"first\", false)", prodConstruction.SuggestedArgumentList);

            // Deconstruction site from `var (a, b) = r;`.
            Assert.IsTrue(result.DeconstructionSites.Count >= 1,
                $"Expected at least 1 deconstruction site, got {result.DeconstructionSites.Count}.");
            var decon = result.DeconstructionSites[0];
            Assert.AreEqual("(a, b)", decon.OriginalPattern);
            Assert.AreEqual("(a, b, _)", decon.SuggestedPattern);

            // Property-pattern sites: the `{ A: 0, B: "" }` and `{ A: var a, B: var b }` patterns
            // name every existing positional field but not NewFlag — both must be flagged as
            // missed correlations, which is the entire point of the tool.
            Assert.IsTrue(result.PropertyPatternSites.Count >= 2,
                $"Expected at least 2 property-pattern sites, got {result.PropertyPatternSites.Count}.");
            Assert.IsTrue(result.PropertyPatternSites.All(p => p.MissedCorrelation),
                "All property patterns name every existing field but not NewFlag — every entry must carry MissedCorrelation=true.");

            // `with { ... }` site from CopyWith.
            Assert.IsTrue(result.WithExpressionSites.Count >= 1,
                $"Expected at least 1 with-expression site, got {result.WithExpressionSites.Count}.");

            // Test-file routing: the file under SampleLib.Tests must be routed into
            // TestFilesConstructing because its containing project is a test project.
            Assert.IsTrue(result.TestFilesConstructing.Any(p => p.EndsWith("TestRecordConsumerTests.cs", StringComparison.OrdinalIgnoreCase)),
                "Test-project consumers must be routed into TestFilesConstructing for dedicated fixture sweep.");

            // Suggested tasks should non-vacuously describe the remediation plan.
            Assert.IsTrue(result.SuggestedTasks.Count >= 2,
                $"Expected at least 2 suggested tasks for a non-empty impact, got {result.SuggestedTasks.Count}.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task PreviewAddition_ForNonPositionalRecord_ReportsOnlyWithExpressionSites()
    {
        // A record declared without a primary constructor (only explicit property declarations)
        // is NOT positional — construction and deconstruction shape are irrelevant, only
        // `with { ... }` consumers are affected by a required new property. This is a critical
        // distinction for reviewers: non-positional adds are lower risk than positional adds.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var libPath = Path.Combine(copiedRoot, "SampleLib", "NonPositionalRecordFixture.cs");
            await File.WriteAllTextAsync(libPath, """
namespace SampleLib.Records;

public record NonPositionalRecord
{
    public int A { get; init; }
    public string B { get; init; } = "";
}
""", CancellationToken.None);

            var consumerPath = Path.Combine(copiedRoot, "SampleApp", "NonPositionalRecordConsumer.cs");
            await File.WriteAllTextAsync(consumerPath, """
using SampleLib.Records;

namespace SampleApp.RecordImpactConsumers;

public static class NonPositionalRecordConsumer
{
    public static NonPositionalRecord MakeOne() => new NonPositionalRecord { A = 1, B = "x" };

    public static NonPositionalRecord CopyWith(NonPositionalRecord original) => original with { A = 2 };
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var result = await RecordFieldAdditionService.PreviewAdditionAsync(
                workspaceId,
                recordMetadataName: "SampleLib.Records.NonPositionalRecord",
                newFieldName: "NewField",
                newFieldType: "string",
                defaultValueExpression: "\"default\"",
                CancellationToken.None);

            Assert.IsFalse(result.IsPositionalRecord,
                "A record without a primary constructor must be reported as non-positional.");
            Assert.AreEqual(0, result.ExistingPositionalParameters.Count);
            Assert.AreEqual(0, result.PositionalConstructionSites.Count,
                "Non-positional records cannot have positional construction-site impact.");
            Assert.AreEqual(0, result.DeconstructionSites.Count,
                "Non-positional records cannot have positional-deconstruction impact.");

            // `with { ... }` still applies — it's the only breaking-change shape for non-positional.
            Assert.IsTrue(result.WithExpressionSites.Count >= 1,
                $"Expected at least 1 with-expression site for non-positional record, got {result.WithExpressionSites.Count}.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task PreviewAddition_ThrowsWhenTargetIsNotRecord()
    {
        // Non-record types (ordinary classes/structs) are out of scope. The service must fail
        // fast with ArgumentException rather than silently returning an empty DTO — callers
        // should know they invoked the wrong tool.
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
            await RecordFieldAdditionService.PreviewAdditionAsync(
                workspaceId,
                recordMetadataName: "SampleLib.Dog",
                newFieldName: "NewFlag",
                newFieldType: "bool",
                defaultValueExpression: null,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task PreviewAddition_ThrowsWhenRecordIsNotFound()
    {
        // Missing metadata-name resolution must produce a structured KeyNotFoundException, which
        // the tool-error handler maps to the NotFound envelope. Silent empty returns would hide
        // typos.
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () =>
            await RecordFieldAdditionService.PreviewAdditionAsync(
                workspaceId,
                recordMetadataName: "SampleLib.NoSuchRecordType",
                newFieldName: "NewFlag",
                newFieldType: "bool",
                defaultValueExpression: null,
                CancellationToken.None));
    }
}
