using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// CS8915: every <c>global using</c> must precede every ordinary using in a file. A source file that
/// is valid (globals first) must still yield a valid generated interface file on every route that
/// shares <c>UsingDirectiveSynthesizer</c>.
/// </summary>
[TestClass]
public sealed class GeneratedInterfaceGlobalUsingOrderIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // Global alias target is a BCL type so it also resolves from the cross-project destination.
    private const string GlobalPlain = "global using System.Globalization;";
    private const string GlobalAlias = "global using GlobalMap = System.Collections.Generic.Dictionary<string, int>;";
    private const string GlobalStatic = "global using static System.Math;";

    private const string ServiceSource = """
        global using System.Globalization;
        global using GlobalMap = System.Collections.Generic.Dictionary<string, int>;
        global using static System.Math;

        using System.Text;
        using System.Threading.Tasks;
        using LocalMap = System.Collections.Generic.List<int>;
        using static System.Console;

        namespace Contracts;

        public class GlobalOrderService
        {
            public Task<StringBuilder> LoadAsync(GlobalMap map, LocalMap list)
            {
                WriteLine(CultureInfo.InvariantCulture.Name);
                return Task.FromResult(new StringBuilder().Append(Sqrt(map.Count + list.Count)));
            }
        }
        """;

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    public async Task Extraction_Emits_Global_Usings_Before_Ordinary_Usings(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "GlobalOrderService.cs");
        // A global alias copied into a generated file of the SAME project duplicates the alias
        // (CS1537) regardless of order, so the standalone route cannot carry one; the alias form is
        // covered on the cross-project route (destination project lacks the alias) and by the
        // pure UsingDirectiveSynthesizerTests.
        var hasGlobalAlias = route == "cross-project";
        var source = hasGlobalAlias
            ? ServiceSource
            : ServiceSource.Replace(GlobalAlias + "\n", "").Replace("GlobalMap map", "System.Collections.Generic.Dictionary<string, int> map");
        await File.WriteAllTextAsync(sourcePath, source);
        await workspace.LoadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        RefactoringPreviewDto preview = route switch
        {
            "standalone" => await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "GlobalOrderService", "IGlobalOrderService", null, false, CancellationToken.None),
            "cross-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "GlobalOrderService", "IGlobalOrderService", "Contracts", CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        var pendingText = await DocumentTextAsync(pending, "IGlobalOrderService.cs");
        await AssertCompilesAsync(pending);
        AssertGlobalsPrecedeOrdinary(pendingText, hasGlobalAlias);

        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "fixture_context_test", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        AssertGlobalsPrecedeOrdinary(await DocumentTextAsync(applied, "IGlobalOrderService.cs"), hasGlobalAlias);
    }

    private static async Task<string> DocumentTextAsync(Solution solution, string name) =>
        (await solution.Projects.SelectMany(p => p.Documents).Single(d => d.Name == name).GetTextAsync()).ToString();

    private static void AssertGlobalsPrecedeOrdinary(string text, bool hasGlobalAlias)
    {
        foreach (var line in hasGlobalAlias ? new[] { GlobalPlain, GlobalAlias, GlobalStatic } : new[] { GlobalPlain, GlobalStatic })
        {
            StringAssert.Contains(text, line, "Global using must be retained.");
        }

        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        var lastGlobal = lines.FindLastIndex(l => l.StartsWith("global using ", StringComparison.Ordinal));
        var firstOrdinary = lines.FindIndex(l => l.StartsWith("using ", StringComparison.Ordinal));
        Assert.IsTrue(firstOrdinary >= 0, "Controls: ordinary usings expected.\n" + text);
        Assert.IsTrue(lastGlobal < firstOrdinary, "Every global using must precede every ordinary using (CS8915).\n" + text);
        StringAssert.Contains(text, "using System.Text;");
        StringAssert.Contains(text, "using System.Threading.Tasks;");
        StringAssert.Contains(text, "using LocalMap = System.Collections.Generic.List<int>;");
        StringAssert.Contains(text, "using static System.Console;");
    }
}
