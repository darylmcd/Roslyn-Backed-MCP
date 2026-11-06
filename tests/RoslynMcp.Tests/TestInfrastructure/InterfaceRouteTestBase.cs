using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// The one fixture and route driver for tests that extract an interface through every public route
/// (standalone, cross-project, dependency-inversion, extract-and-wire) from a type in <c>SampleLib</c> into
/// <c>Contracts</c>, with a <c>Consumer</c> that references both.
/// </summary>
public abstract class InterfaceRouteTestBase : IsolatedWorkspaceTestBase
{
    /// <summary>
    /// Builds the SampleLib/Contracts/Consumer solution around <paramref name="sourceText"/> (the only compiled
    /// SampleLib file) and loads it. When <paramref name="sharedSource"/> is given, a <c>Shared</c> project holds it and
    /// both SampleLib and Contracts reference it, so signature types the receiver must name have an owner it can see.
    /// <paramref name="projectProperties"/> maps a project name to MSBuild properties added to that project (for example
    /// <c>AllowUnsafeBlocks</c>); it is applied before the first load.
    /// </summary>
    protected static async Task<string> PrepareInterfaceRouteFixtureAsync(
        IsolatedWorkspaceScope workspace, string sourceFileName, string sourceText, string consumerFileName, string consumerText,
        string? sharedSource = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? projectProperties = null)
    {
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        AddProjectToCopiedSolution(workspace.RootPath, "Consumer", "net10.0");
        var kept = new List<string> { "SampleLib/SampleLib.csproj", "Contracts/Contracts.csproj", "Consumer/Consumer.csproj" };
        if (sharedSource is not null)
        {
            AddProjectToCopiedSolution(workspace.RootPath, "Shared", "net10.0");
            kept.Add("Shared/Shared.csproj");
        }

        var solution = XDocument.Load(workspace.SolutionPath);
        foreach (var project in solution.Descendants("Project").Where(p => !kept.Contains(p.Attribute("Path")!.Value.Replace('\\', '/'))).ToArray())
            project.Remove();
        solution.Save(workspace.SolutionPath);

        const string SharedReference = "../Shared/Shared.csproj";
        void AddItems(string projectName, params XElement[] items)
        {
            var path = workspace.GetPath(projectName, projectName + ".csproj");
            var xml = XDocument.Load(path);
            xml.Root!.Add(new XElement("ItemGroup", items));
            xml.Save(path);
        }

        XElement Reference(string include) => new("ProjectReference", new XAttribute("Include", include));
        var libPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var lib = XDocument.Load(libPath);
        lib.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultCompileItems", "false")));
        lib.Save(libPath);
        var libItems = new List<XElement> { new("Compile", new XAttribute("Include", sourceFileName)) };
        if (sharedSource is not null)
        {
            libItems.Add(Reference(SharedReference));
            AddItems("Contracts", Reference(SharedReference));
        }

        AddItems("SampleLib", libItems.ToArray());
        AddItems("Consumer", Reference("../SampleLib/SampleLib.csproj"), Reference("../Contracts/Contracts.csproj"));

        var sourcePath = workspace.GetPath("SampleLib", sourceFileName);
        foreach (var (projectName, properties) in projectProperties ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
        {
            var path = workspace.GetPath(projectName, projectName + ".csproj");
            var xml = XDocument.Load(path);
            xml.Root!.Add(new XElement("PropertyGroup", properties.Select(p => new XElement(p.Key, p.Value))));
            xml.Save(path);
        }

        await File.WriteAllTextAsync(sourcePath, sourceText);
        await File.WriteAllTextAsync(workspace.GetPath("Consumer", consumerFileName), consumerText);
        if (sharedSource is not null) await File.WriteAllTextAsync(workspace.GetPath("Shared", "SharedLib.cs"), sharedSource);
        await workspace.LoadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        return sourcePath;
    }

    /// <summary>Previews extracting <paramref name="typeName"/> as <c>I{typeName}</c> on <paramref name="route"/>.</summary>
    protected static Task<RefactoringPreviewDto> PreviewInterfaceRouteAsync(
        string route, string workspaceId, string sourcePath, string typeName, CompositePreviewStore stores, IReadOnlyList<string>? memberNames = null) => route switch
        {
            "standalone" => InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspaceId, sourcePath, typeName, "I" + typeName, memberNames, false, CancellationToken.None),
            "cross-project" => CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                workspaceId, sourcePath, typeName, "I" + typeName, "Contracts", CancellationToken.None),
            "dependency-inversion" => CrossProjectRefactoringService.PreviewDependencyInversionAsync(
                workspaceId, sourcePath, typeName, "I" + typeName, "Contracts", CancellationToken.None),
            "extract-and-wire" => new ExtractAndWireOrchestrator(WorkspaceManager, stores, PreviewStore, CrossProjectRefactoringService, DiRegistrationService)
                .PreviewExtractAndWireInterfaceAsync(workspaceId, sourcePath, typeName, "I" + typeName, "Contracts", true, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
}
