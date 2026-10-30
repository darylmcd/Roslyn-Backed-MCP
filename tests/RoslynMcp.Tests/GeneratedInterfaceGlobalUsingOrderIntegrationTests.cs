using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// Same-project interface files use existing project-global directives without copying them.
/// Cross-project interface files copy globals before ordinary usings (CS8915).
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
            public Task<StringBuilder> LoadAsync(GlobalMap map, LocalMap list, CultureInfo culture)
            {
                WriteLine(culture.Name);
                return Task.FromResult(new StringBuilder().Append(Sqrt(map.Count + list.Count)));
            }
        }
        """;

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("same-project")]
    [DataRow("cross-project")]
    [DataRow("cross-project-reverse")]
    public async Task Extraction_UsesDestinationGlobalPlainUsingFromSeparateFile(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "SeparateGlobalService.cs");
        var isCrossProject = route.StartsWith("cross-project", StringComparison.Ordinal);
        if (!isCrossProject)
        {
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "GlobalUsings.cs"),
                "global using System.Globalization;\n");
        }
        await File.WriteAllTextAsync(workspace.GetPath("Contracts", "GlobalUsings.cs"),
            route == "cross-project-reverse"
                ? "global using System.Globalization;\n"
                : "global using global::System.Globalization;\n");
        var sourceGlobal = route switch
        {
            "cross-project" => "global using System.Globalization;\n\n",
            "cross-project-reverse" => "global using global::System.Globalization;\n\n",
            _ => "",
        };
        var source = sourceGlobal + """
            namespace Contracts;

            public class SeparateGlobalService
            {
                public CultureInfo Load(CultureInfo culture) => culture;
            }
            """;
        await File.WriteAllTextAsync(sourcePath, source);
        await workspace.LoadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        RefactoringPreviewDto preview = route switch
        {
            "standalone" => await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "SeparateGlobalService", "ISeparateGlobalService", null, false, CancellationToken.None),
            "same-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "SeparateGlobalService", "ISeparateGlobalService", "SampleLib", CancellationToken.None),
            "cross-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "SeparateGlobalService", "ISeparateGlobalService", "Contracts", CancellationToken.None),
            "cross-project-reverse" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "SeparateGlobalService", "ISeparateGlobalService", "Contracts", CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        await AssertCompilesAsync(pending);
        var generatedText = await DocumentTextAsync(pending, "ISeparateGlobalService.cs");
        CollectionAssert.DoesNotContain(generatedText.Split('\n').Select(line => line.Trim()).ToArray(),
            "using System.Globalization;", "Destination global using already supplies CultureInfo.");
        Assert.IsFalse(generatedText.Contains("global using ", StringComparison.Ordinal),
            "Cross-project source global plain using is redundant with the destination global using.");
        StringAssert.Contains(generatedText, "CultureInfo Load(CultureInfo culture);");
    }

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("same-project")]
    [DataRow("cross-project")]
    public async Task Extraction_Emits_Global_Usings_Before_Ordinary_Usings(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "GlobalOrderService.cs");
        await File.WriteAllTextAsync(sourcePath, ServiceSource);
        await workspace.LoadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));

        RefactoringPreviewDto preview = route switch
        {
            "standalone" => await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "GlobalOrderService", "IGlobalOrderService", null, false, CancellationToken.None),
            "same-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "GlobalOrderService", "IGlobalOrderService", "SampleLib", CancellationToken.None),
            "cross-project" => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                sourcePath, "GlobalOrderService", "IGlobalOrderService", "Contracts", CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        var pending = await ReparseSerializedDocumentsAsync(PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution);
        var pendingText = await DocumentTextAsync(pending, "IGlobalOrderService.cs");
        await AssertCompilesAsync(pending);
        AssertGlobalsPrecedeOrdinary(pendingText, route == "cross-project");

        var result = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "fixture_context_test", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertNativeContextAsync(WorkspaceManager, workspace.WorkspaceId);
        var applied = await ReparseSerializedDocumentsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        await AssertCompilesAsync(applied);
        AssertGlobalsPrecedeOrdinary(await DocumentTextAsync(applied, "IGlobalOrderService.cs"), route == "cross-project");
    }

    private static async Task<string> DocumentTextAsync(Solution solution, string name) =>
        (await solution.Projects.SelectMany(p => p.Documents).Single(d => d.Name == name).GetTextAsync()).ToString();

    private static void AssertGlobalsPrecedeOrdinary(string text, bool copiesGlobals)
    {
        foreach (var line in new[] { GlobalPlain, GlobalAlias, GlobalStatic })
        {
            Assert.AreEqual(copiesGlobals, text.Contains(line, StringComparison.Ordinal),
                $"Global using should be copied only for cross-project extraction: {line}\n{text}");
        }

        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        CollectionAssert.DoesNotContain(lines, "using System.Globalization;",
            "A source global plain using already supplies the interface signature namespace.");
        var lastGlobal = lines.FindLastIndex(l => l.StartsWith("global using ", StringComparison.Ordinal));
        var firstOrdinary = lines.FindIndex(l => l.StartsWith("using ", StringComparison.Ordinal));
        Assert.IsTrue(firstOrdinary >= 0, "Controls: ordinary usings expected.\n" + text);
        Assert.IsTrue(lastGlobal < firstOrdinary, "Every global using must precede every ordinary using (CS8915).\n" + text);
        StringAssert.Contains(text, "using System.Text;");
        CollectionAssert.DoesNotContain(lines, "using System.Threading.Tasks;",
            "Destination project implicit global using already supplies Task.");
        StringAssert.Contains(text, "using LocalMap = System.Collections.Generic.List<int>;");
        StringAssert.Contains(text, "using static System.Console;");
    }
}
