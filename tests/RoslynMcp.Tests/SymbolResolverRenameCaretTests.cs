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
}
