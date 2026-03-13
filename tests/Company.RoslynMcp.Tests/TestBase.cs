using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn;
using Company.RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Company.RoslynMcp.Tests;

public abstract class TestBase
{
    private static readonly object _initLock = new();
    private static bool _msbuildInitialized;

    protected static IPreviewStore PreviewStore { get; private set; } = null!;
    protected static WorkspaceManager WorkspaceManager { get; private set; } = null!;
    protected static SymbolService SymbolService { get; private set; } = null!;
    protected static DiagnosticService DiagnosticService { get; private set; } = null!;
    protected static RefactoringService RefactoringService { get; private set; } = null!;
    protected static string SampleSolutionPath { get; private set; } = null!;

    protected static void InitializeServices()
    {
        lock (_initLock)
        {
            if (!_msbuildInitialized)
            {
                MsBuildInitializer.EnsureInitialized();
                _msbuildInitialized = true;
            }
        }

        PreviewStore = new PreviewStore();
        WorkspaceManager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            PreviewStore);
        SymbolService = new SymbolService(
            WorkspaceManager,
            NullLogger<SymbolService>.Instance);
        DiagnosticService = new DiagnosticService(
            WorkspaceManager,
            NullLogger<DiagnosticService>.Instance);
        RefactoringService = new RefactoringService(
            WorkspaceManager,
            PreviewStore,
            NullLogger<RefactoringService>.Instance);

        // Find SampleSolution path relative to test execution directory
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "samples", "SampleSolution", "SampleSolution.slnx");
            if (File.Exists(candidate))
            {
                SampleSolutionPath = candidate;
                return;
            }
            // Also check for .sln
            candidate = Path.Combine(dir, "samples", "SampleSolution", "SampleSolution.sln");
            if (File.Exists(candidate))
            {
                SampleSolutionPath = candidate;
                return;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find SampleSolution. Ensure the samples directory exists at the repo root.");
    }

    protected static void DisposeServices()
    {
        WorkspaceManager?.Dispose();
    }

    protected static string CreateSampleSolutionCopy()
    {
        var sampleRoot = Path.GetDirectoryName(SampleSolutionPath)
            ?? throw new InvalidOperationException("Sample solution root could not be resolved.");
        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
        CopyDirectory(sampleRoot, tempRoot);

        var slnxPath = Path.Combine(tempRoot, "SampleSolution.slnx");
        if (File.Exists(slnxPath))
        {
            return slnxPath;
        }

        var slnPath = Path.Combine(tempRoot, "SampleSolution.sln");
        if (File.Exists(slnPath))
        {
            return slnPath;
        }

        throw new InvalidOperationException("Copied sample solution is missing a solution file.");
    }

    protected static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var destinationSubdirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, destinationSubdirectory);
        }
    }
}
