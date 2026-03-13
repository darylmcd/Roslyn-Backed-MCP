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
}
