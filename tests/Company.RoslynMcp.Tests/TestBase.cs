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
    protected static ValidationService ValidationService { get; private set; } = null!;
    protected static string RepositoryRootPath { get; private set; } = null!;
    protected static string SampleSolutionPath { get; private set; } = null!;
    protected static string BuildFailureSolutionPath { get; private set; } = null!;
    protected static string GeneratedDocumentSolutionPath { get; private set; } = null!;

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
            PreviewStore,
            new FileWatcherService(NullLogger<FileWatcherService>.Instance));
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
        ValidationService = new ValidationService(
            WorkspaceManager,
            new DotnetCommandRunner(),
            NullLogger<ValidationService>.Instance);

        RepositoryRootPath = FindRepositoryRoot();
        SampleSolutionPath = FindFixturePath("SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        BuildFailureSolutionPath = FindFixturePath("BuildFailureSolution", "BuildFailureSolution.slnx", "BuildFailureSolution.sln");
        GeneratedDocumentSolutionPath = FindFixturePath("GeneratedDocumentSolution", "GeneratedDocumentSolution.slnx", "GeneratedDocumentSolution.sln");
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
        CopyRepositorySupportFiles(tempRoot);

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

    protected static string CreateFixtureCopy(string fixtureSolutionPath)
    {
        var fixtureRoot = Path.GetDirectoryName(fixtureSolutionPath)
            ?? throw new InvalidOperationException("Fixture root could not be resolved.");
        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"));
        CopyDirectory(fixtureRoot, tempRoot);
        CopyRepositorySupportFiles(tempRoot);
        return Path.Combine(tempRoot, Path.GetFileName(fixtureSolutionPath));
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

    private static string FindFixturePath(string fixtureDirectory, params string[] candidateFiles)
    {
        var dir = RepositoryRootPath;
        while (dir is not null)
        {
            foreach (var candidateFile in candidateFiles)
            {
                var candidate = Path.Combine(dir, "samples", fixtureDirectory, candidateFile);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find fixture '{fixtureDirectory}'. Ensure the samples directory exists at the repo root.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "RoslynMcp.slnx")) &&
                File.Exists(Path.Combine(dir, "Directory.Build.props")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private static void CopyRepositorySupportFiles(string destinationRoot)
    {
        foreach (var fileName in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json" })
        {
            var sourcePath = Path.Combine(RepositoryRootPath, fileName);
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, Path.Combine(destinationRoot, fileName), overwrite: true);
            }
        }
    }
}
