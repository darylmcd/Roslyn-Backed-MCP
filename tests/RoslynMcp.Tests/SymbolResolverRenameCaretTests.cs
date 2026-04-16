using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SymbolResolverRenameCaretTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ResolveAtPosition_TupleElement_PrefersLocalOverEnclosingMethod()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var probePath = Path.Combine(workspace.GetPath("SampleLib"), "TupleRenameCaretProbe.cs");
        await File.WriteAllTextAsync(
            probePath,
            """
            namespace SampleLib;

            public sealed class TupleRenameCaretProbe
            {
                public void M()
                {
                    var (left, right) = (1, 2);
                    _ = left;
                }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var lines = await File.ReadAllLinesAsync(probePath, CancellationToken.None).ConfigureAwait(false);
        var lineIdx = Array.FindIndex(lines, l => l.Contains("(left, right)", StringComparison.Ordinal));
        Assert.IsTrue(lineIdx >= 0, "Expected deconstruction line.");
        var line = lineIdx + 1;
        var col = lines[lineIdx].IndexOf("left", StringComparison.Ordinal) + 1;

        var sym = await SymbolResolver.ResolveAtPositionAsync(solution, probePath, line, col, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsNotNull(sym);
        Assert.AreEqual(SymbolKind.Local, sym!.Kind);
        Assert.AreEqual("left", sym.Name);
    }

    [TestMethod]
    public async Task ResolveAtPosition_MethodName_ResolvesMethod()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var probePath = Path.Combine(workspace.GetPath("SampleLib"), "TupleRenameCaretProbe2.cs");
        await File.WriteAllTextAsync(
            probePath,
            """
            namespace SampleLib;

            public sealed class TupleRenameCaretProbe2
            {
                public void MyMethod() { }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var lines = await File.ReadAllLinesAsync(probePath, CancellationToken.None).ConfigureAwait(false);
        var lineIdx = Array.FindIndex(lines, l => l.Contains("MyMethod", StringComparison.Ordinal));
        Assert.IsTrue(lineIdx >= 0);
        var line = lineIdx + 1;
        var col = lines[lineIdx].IndexOf("MyMethod", StringComparison.Ordinal) + 1;

        var sym = await SymbolResolver.ResolveAtPositionAsync(solution, probePath, line, col, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.IsNotNull(sym);
        Assert.AreEqual(SymbolKind.Method, sym!.Kind);
        Assert.AreEqual("MyMethod", sym.Name);
    }

    // schema-drift-jellyfin-audit (metadata-name member lookup sub-item): before this fix,
    // `ResolveByMetadataNameAsync` only called `Compilation.GetTypeByMetadataName`, so an agent
    // passing `SampleLib.AnimalService.GetAllAnimals` (a METHOD metadata name) got null because
    // the full path doesn't resolve as a type. Post-fix the resolver splits at the last dot and
    // tries the member.

    [TestMethod]
    public async Task ResolveByMetadataName_ResolvesMethodOnType_AfterTypeLookupFails()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);

        var member = await SymbolResolver.ResolveByMetadataNameAsync(
            solution, "SampleLib.AnimalService.GetAllAnimals", CancellationToken.None);

        Assert.IsNotNull(member, "GetAllAnimals should resolve as a method on SampleLib.AnimalService after type lookup fallback.");
        Assert.AreEqual(SymbolKind.Method, member!.Kind);
        Assert.AreEqual("GetAllAnimals", member.Name);
        Assert.AreEqual("AnimalService", member.ContainingType?.Name);
    }

    [TestMethod]
    public async Task ResolveByMetadataName_TypeTakesPriorityOverMemberPath()
    {
        // Control test: calling with a full type metadata name still returns the type, not
        // some namespace-member ambiguity.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);

        var type = await SymbolResolver.ResolveByMetadataNameAsync(
            solution, "SampleLib.AnimalService", CancellationToken.None);

        Assert.IsNotNull(type);
        Assert.AreEqual(SymbolKind.NamedType, type!.Kind);
    }

    [TestMethod]
    public async Task ResolveByMetadataName_UnknownMember_ReturnsNull()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);

        var sym = await SymbolResolver.ResolveByMetadataNameAsync(
            solution, "SampleLib.AnimalService.NoSuchMember", CancellationToken.None);

        Assert.IsNull(sym);
    }

    // symbol-info-lenient-whitespace-resolution: strict mode rejects a caret on whitespace
    // adjacent to an identifier. Lenient mode (the legacy default when strict is omitted)
    // still resolves to the adjacent token via the preceding-token fallback.
    [TestMethod]
    public async Task ResolveAtPosition_WhitespaceLeadingToIdentifier_StrictReturnsNull_LenientResolves()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var probePath = Path.Combine(workspace.GetPath("SampleLib"), "WhitespaceStrictProbe.cs");
        await File.WriteAllTextAsync(
            probePath,
            """
            namespace SampleLib;

            public sealed class WhitespaceStrictProbe
            {
                public int Value;
            }
            """,
            CancellationToken.None).ConfigureAwait(false);
        await workspace.ReloadAsync(CancellationToken.None).ConfigureAwait(false);

        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);

        // `    public int Value;` — column 1 is leading whitespace (the indent).
        // Lenient mode's preceding-token fallback would drift to the previous identifier
        // (the `{` before this line isn't a symbol, but on a richer shape it would be).
        // Strict mode must return null without drifting.
        var strictResult = await SymbolResolver.ResolveAtPositionAsync(
            solution, probePath, line: 5, column: 1, CancellationToken.None, strict: true)
            .ConfigureAwait(false);
        Assert.IsNull(strictResult, "Strict mode must reject a caret on leading whitespace.");

        // On an identifier directly, strict mode still resolves — the flag only changes the
        // leading-trivia / preceding-token fallback, not exact-token lookups.
        var lines = await File.ReadAllLinesAsync(probePath, CancellationToken.None).ConfigureAwait(false);
        var fieldLine = Array.FindIndex(lines, l => l.Contains("public int Value", StringComparison.Ordinal)) + 1;
        var valueCol = lines[fieldLine - 1].IndexOf("Value", StringComparison.Ordinal) + 1;

        var strictHit = await SymbolResolver.ResolveAtPositionAsync(
            solution, probePath, fieldLine, valueCol, CancellationToken.None, strict: true)
            .ConfigureAwait(false);
        Assert.IsNotNull(strictHit, "Strict mode must still resolve a caret on the identifier itself.");
        Assert.AreEqual("Value", strictHit!.Name);
    }
}
