using Microsoft.CodeAnalysis;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

public abstract class TypeExtractionTestBase : IsolatedWorkspaceTestBase
{
    private readonly List<string> _directoriesToDelete = [];

    protected IReadOnlyList<string> QueuedDirectories => _directoriesToDelete;

    [TestCleanup]
    public async Task TestCleanup() =>
        await CleanupFailureCollector.DeleteDirectoriesAsync(
            _directoriesToDelete,
            TestFixtureFileSystem.DeleteDirectoryIfExists);

    protected string CreateQueuedSampleSolutionCopy()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        QueueDirectoryForCleanup(Path.GetDirectoryName(copiedSolutionPath)!);
        return copiedSolutionPath;
    }

    protected void QueueDirectoryForCleanup(string path) => _directoriesToDelete.Add(path);

    protected Task<(string WorkspaceId, string FilePath, string SolutionDirectory)> CreateExtractionFixtureAsync(
        string fileName,
        string source) => CreateExtractionFixtureAsync(fileName, source, []);

    /// <param name="extraFiles">Additional fixture files, relative to the SampleLib directory (so <c>../SampleApp/X.cs</c> targets a dependent project).</param>
    protected async Task<(string WorkspaceId, string FilePath, string SolutionDirectory)> CreateExtractionFixtureAsync(
        string fileName,
        string source,
        IReadOnlyList<(string RelativePath, string Text)> extraFiles)
    {
        var copiedSolutionPath = CreateQueuedSampleSolutionCopy();
        var solutionDirectory = Path.GetDirectoryName(copiedSolutionPath)!;
        var filePath = Path.Combine(solutionDirectory, "SampleLib", fileName);
        await File.WriteAllTextAsync(filePath, source);
        foreach (var (relativePath, text) in extraFiles)
            await File.WriteAllTextAsync(Path.GetFullPath(Path.Combine(solutionDirectory, "SampleLib", relativePath)), text);
        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (loadResult.WorkspaceId, filePath, solutionDirectory);
    }

    /// <summary>
    /// Acceptance gate for type-extraction-composition-constructor-coverage: the previewed source
    /// (updated source type + generated new type) must compile with zero errors — specifically none
    /// of CS1737 (required after optional), CS1729 (no matching constructor overload for the
    /// `this(...)` chain), or CS0191 (readonly field assigned outside a constructor).
    /// </summary>
    protected static async Task AssertModifiedSolutionCompilesAsync(string previewToken)
    {
        var retrieved = PreviewStore.Retrieve(previewToken);
        Assert.IsNotNull(retrieved, "the preview token must be redeemable immediately after the preview");
        var project = retrieved.Value.ModifiedSolution.Projects
            .FirstOrDefault(p => string.Equals(p.Name, "SampleLib", StringComparison.Ordinal));
        Assert.IsNotNull(project, "modified solution must contain the SampleLib project");
        var compilation = await project.GetCompilationAsync(CancellationToken.None);
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.AreEqual(0, errors.Length,
            "the previewed solution must compile clean (no CS1737/CS1729/CS0191 or any other error). " +
            $"Errors:\n{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>
    /// Emits the previewed <c>SampleLib</c> project, builds the extracted helper, passes it to the host type's
    /// composition constructor, and returns the host's int property.
    /// </summary>
    protected static async Task<int> EmitAndReadHostPropertyAsync(
        string previewToken, string hostTypeName, string helperTypeName, string propertyName)
    {
        var project = PreviewStore.Retrieve(previewToken)!.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib");
        var compilation = (await project.GetCompilationAsync(CancellationToken.None))!;
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var context = new System.Runtime.Loader.AssemblyLoadContext(hostTypeName, isCollectible: true);
        try
        {
            image.Position = 0;
            var assembly = context.LoadFromStream(image);
            var helper = Activator.CreateInstance(assembly.GetType("SampleLib." + helperTypeName)!);
            var hostType = assembly.GetType("SampleLib." + hostTypeName)!;
            var host = Activator.CreateInstance(hostType, helper)!;
            return (int)hostType.GetProperty(propertyName)!.GetValue(host)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
