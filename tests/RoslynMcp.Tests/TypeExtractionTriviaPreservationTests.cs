using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// extract-type-preserve-untouched-trivia: extract_type formats only the syntax it synthesizes or
/// changes. Every other byte of the source file, including deliberately unusual whitespace and the
/// file's own line endings, must survive preview and apply unchanged.
/// </summary>
[TestClass]
public sealed class TypeExtractionTriviaPreservationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    public static IEnumerable<object[]> Cases()
    {
        foreach (var eol in new[] { "\r\n", "\n" })
            foreach (var shape in new[] { "implicit", "block", "optional", "chained", "expression" })
                yield return [eol, shape];
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task ExtractType_Preserves_Untouched_Source_Bytes(string eol, string shape)
    {
        string[] block = ["    {", "        _seed  =  seed ;   // keep", "    }"];
        string[] constructor = shape switch
        {
            "block" => ["    public TriviaFixture( int seed )", .. block],
            "optional" => ["    public TriviaFixture( int seed, string tag = \"x\" )", .. block],
            "chained" => ["    public TriviaFixture( int seed )", .. block, "    public TriviaFixture( ) : this( 7 ) { }"],
            "expression" => ["    public TriviaFixture( int seed ) => _seed  =  seed ;"],
            _ => [],
        };
        string[] Lines(string[] header, string[] ctor, string useCompute, bool withCompute) =>
        [
            "namespace SampleLib;",
            "",
            "// header   comment   with   runs",
            "public class TriviaFixture",
            "{",
            .. header,
            "    private   int   _seed  =  3 ;      // odd spacing",
            "",
            "",
            .. ctor,
            "    public int Keep( int x )=>x+_seed ;",
            "\tpublic int Tabbed() { return  1; }",
            .. withCompute ? new[] { "    public int Compute(int x) => x * 2;" } : [],
            useCompute,
            "}",
            "",
            "public  class   Neighbor  {  public void  M( ) { }  }",
            "",
        ];

        var original = string.Join(eol, Lines([], constructor, "    public int UseCompute() => Compute( 5 );", withCompute: true));
        string[] synthesized = shape != "implicit"
            ? ["    private readonly ComputeHelper _computeHelper;"]
            :
            [
                "    private readonly ComputeHelper _computeHelper;",
                "",
                "    public TriviaFixture(ComputeHelper computeHelper)",
                "    {",
                "        _computeHelper = computeHelper;",
                "    }",
                "",
            ];
        string[] assigned = ["    {", "        _computeHelper = computeHelper;", "        _seed  =  seed ;   // keep", "    }"];
        string[] rewrittenConstructor = shape switch
        {
            "block" => ["    public TriviaFixture( int seed, ComputeHelper computeHelper )", .. assigned],
            "optional" => ["    public TriviaFixture( int seed, ComputeHelper computeHelper, string tag = \"x\" )", .. assigned],
            "chained" => ["    public TriviaFixture( int seed, ComputeHelper computeHelper )", .. assigned,
                "    public TriviaFixture( ComputeHelper computeHelper) : this( 7, computeHelper ) { }"],
            "expression" => ["    public TriviaFixture( int seed, ComputeHelper computeHelper )", "    {", "        _computeHelper = computeHelper;", "        _seed = seed;", "    }"],
            _ => [],
        };
        var expected = string.Join(eol, Lines(synthesized, rewrittenConstructor, "    public int UseCompute() => _computeHelper.Compute( 5 );", withCompute: false));

        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "TriviaFixture.cs");
        await File.WriteAllTextAsync(path, original);
        await workspace.LoadAsync();

        var preview = await TypeExtractionService.PreviewExtractTypeAsync(
            workspace.WorkspaceId, path, "TriviaFixture", ["Compute"], "ComputeHelper", null, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var document = pending.Projects.SelectMany(p => p.Documents).Single(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        var previewText = (await document.GetTextAsync()).ToString();
        Assert.AreEqual(Visible(expected), Visible(previewText));
        Assert.AreEqual(original, await File.ReadAllTextAsync(path), "Preview must not touch disk.");

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "extract_type_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(path), Visible(await File.ReadAllTextAsync(path)));
    }

    private static string Visible(string text) => text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
}
