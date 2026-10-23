using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

public abstract class IsolatedWorkspaceTestBase : TestBase
{
    /// <summary>Creates and restores a fresh single-project graph; callers write source before loading it.</summary>
    protected static Task<IsolatedWorkspaceScope> CreateNativeProjectFixtureAsync(string projectName, CancellationToken ct)
    {
        var root = Path.Combine(TestTempRoot.Current, "NativeProject", Guid.NewGuid().ToString("N"));
        var scope = CreateWorkspaceScope(root, Path.Combine(root, "Fixture.slnx"), WorkspaceManager);
        return InitializeWithCleanupAsync(scope, async (workspace, token) =>
        {
            Directory.CreateDirectory(workspace.GetPath(projectName));
            await File.WriteAllTextAsync(workspace.GetPath("global.json"), "{}", token);
            await File.WriteAllTextAsync(workspace.GetPath("Directory.Build.props"), "<Project />", token);
            var solution = new XDocument(new XElement("Solution",
                new XElement("Project", new XAttribute("Path", $"{projectName}/{projectName}.csproj"))));
            await File.WriteAllTextAsync(workspace.SolutionPath, solution.ToString(), token);
            await File.WriteAllTextAsync(workspace.GetPath(projectName, projectName + ".csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                </Project>
                """, token);
            await RestoreWorkspaceAsync(workspace, token).ConfigureAwait(false);
        }, ct);
    }

    protected static IsolatedWorkspaceScope CreateIsolatedWorkspaceCopy()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var rootPath = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Isolated workspace root could not be resolved.");

        return CreateWorkspaceScope(rootPath, solutionPath, WorkspaceManager);
    }

    protected internal static IsolatedWorkspaceScope CreateWorkspaceScope(
        string rootPath, string solutionPath, WorkspaceManager workspaceManager) =>
        new(rootPath, solutionPath, workspaceManager,
            (path, ct) => workspaceManager.LoadAsync(path,
                OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>()), ct));

    protected static async Task<IsolatedWorkspaceScope> CreateIsolatedWorkspaceAsync(CancellationToken ct = default)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        return await InitializeWithCleanupAsync(
            workspace,
            static async (scope, token) =>
            {
                _ = await scope.LoadAsync(token).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);
    }

    protected static Task RestoreWorkspaceAsync(
        IsolatedWorkspaceScope workspace,
        CancellationToken ct = default) =>
        RestoreWorkspaceAsync(workspace, DotnetCommandRunner, ct);

    internal static async Task RestoreWorkspaceAsync(
        IsolatedWorkspaceScope workspace,
        IDotnetCommandRunner commandRunner,
        CancellationToken ct = default)
    {
        await RunFixtureRestoreAsync(workspace.RootPath, workspace.SolutionPath,
            ["restore", workspace.SolutionPath, "--nologo"], commandRunner, ct).ConfigureAwait(false);
    }

    protected static Task RestoreCopiedSolutionAsync(
        string solutionPath,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Copied fixture root is missing.");
        // The pre-restored sample records the package installation used by the copied graph.
        // Do not infer a cache from the newly added project, which has never been restored.
        var packages = RoslynMcp.Roslyn.Services.RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(
            Path.Combine(root, "SampleLib", "obj", "project.assets.json"), ct)
            ?? throw new AssertFailedException("The copied sample must establish its restore package root.");
        if (!Path.IsPathFullyQualified(packages))
        {
            throw new AssertFailedException("The copied sample package root must be absolute.");
        }

        var loadProperties = RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(solutionPath, properties);
        var arguments = RoslynMcp.Roslyn.Services.MsBuildCommandProperties.Append(
            ["restore", solutionPath, "--nologo", "--packages", packages], loadProperties);
        return RunFixtureRestoreAsync(root, solutionPath, arguments, DotnetCommandRunner, ct);
    }

    private static async Task RunFixtureRestoreAsync(
        string rootPath,
        string solutionPath,
        IReadOnlyList<string> arguments,
        IDotnetCommandRunner commandRunner,
        CancellationToken ct)
    {
        using var restoreDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        restoreDeadline.CancelAfter(TimeSpan.FromMinutes(2));
        var execution = await commandRunner.RunAsync(
            workingDirectory: rootPath,
            targetPath: solutionPath,
            arguments: arguments,
            restoreDeadline.Token).ConfigureAwait(false);

        Assert.IsTrue(
            execution.Succeeded,
            $"dotnet restore failed for test fixture. ExitCode={execution.ExitCode} " +
            $"StdOut={execution.StdOut} StdErr={execution.StdErr}");
    }

    internal static async Task<TResource> InitializeWithCleanupAsync<TResource>(
        TResource resource,
        Func<TResource, CancellationToken, Task> initializeAsync,
        CancellationToken ct = default)
        where TResource : IAsyncDisposable
    {
        try
        {
            await initializeAsync(resource, ct).ConfigureAwait(false);
            return resource;
        }
        catch (Exception initializationFailure)
        {
            try
            {
                await resource.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Resource initialization and cleanup both failed.",
                    initializationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    protected static void AddProjectToCopiedSolution(
        string copiedRoot,
        string projectName,
        string targetFramework)
    {
        var projectDirectory = Path.Combine(copiedRoot, projectName);
        Directory.CreateDirectory(projectDirectory);

        var projectFilePath = Path.Combine(projectDirectory, projectName + ".csproj");
        File.WriteAllText(
            projectFilePath,
            $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>{targetFramework}</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");

        var solutionFilePath = Path.Combine(copiedRoot, "SampleSolution.slnx");
        var solutionDocument = XDocument.Load(solutionFilePath, LoadOptions.PreserveWhitespace);
        solutionDocument.Root?.Add(
            new XElement("Project", new XAttribute("Path", $"{projectName}/{projectName}.csproj")));
        solutionDocument.Save(solutionFilePath, SaveOptions.DisableFormatting);
    }

    protected internal sealed class IsolatedWorkspaceScope : IAsyncDisposable, IDisposable
    {
        private readonly IWorkspaceManager _workspaceManager;
        private readonly Func<string, CancellationToken, Task<WorkspaceStatusDto>> _loadWorkspace;
        private readonly Func<string, bool> _closeWorkspace;
        private readonly Action<string> _deleteRoot;
        private int _disposeState;
        private string? _workspaceId;

        internal IsolatedWorkspaceScope(string rootPath, string solutionPath, IWorkspaceManager workspaceManager,
            Func<string, CancellationToken, Task<WorkspaceStatusDto>> loadWorkspace)
            : this(
                rootPath,
                solutionPath,
                workspaceId: null,
                workspaceManager,
                loadWorkspace,
                workspaceManager.Close,
                DeleteDirectoryIfExists)
        {
        }

        internal IsolatedWorkspaceScope(
            string rootPath,
            string solutionPath,
            string? workspaceId,
            IWorkspaceManager workspaceManager,
            Func<string, CancellationToken, Task<WorkspaceStatusDto>> loadWorkspace,
            Func<string, bool> closeWorkspace,
            Action<string> deleteRoot)
        {
            _workspaceManager = workspaceManager;
            _loadWorkspace = loadWorkspace;
            RootPath = rootPath;
            SolutionPath = solutionPath;
            _workspaceId = workspaceId;
            _closeWorkspace = closeWorkspace;
            _deleteRoot = deleteRoot;
        }

        public string RootPath { get; }

        public string SolutionPath { get; }

        public string WorkspaceId => _workspaceId ?? throw new InvalidOperationException("Workspace has not been loaded yet.");

        public async Task<string> LoadAsync(CancellationToken ct = default)
        {
            if (_workspaceId is null)
            {
                var status = await _loadWorkspace(SolutionPath, ct).ConfigureAwait(false);
                _workspaceId = status.WorkspaceId;
            }

            return _workspaceId;
        }

        public async Task ReloadAsync(CancellationToken ct = default)
        {
            if (_workspaceId is null)
            {
                await LoadAsync(ct).ConfigureAwait(false);
                return;
            }

            await _workspaceManager.ReloadAsync(_workspaceId, ct).ConfigureAwait(false);
        }

        public string GetPath(params string[] segments)
        {
            return segments.Length == 0 ? RootPath : Path.Combine([RootPath, .. segments]);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            Exception? closeFailure = null;
            Exception? deleteFailure = null;
            try
            {
                if (_workspaceId is not null)
                {
                    _closeWorkspace(_workspaceId);
                }
            }
            catch (Exception ex)
            {
                closeFailure = ex;
            }

            try
            {
                _deleteRoot(RootPath);
            }
            catch (Exception ex)
            {
                deleteFailure = ex;
            }

            ThrowCleanupFailures(closeFailure, deleteFailure);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static void ThrowCleanupFailures(Exception? closeFailure, Exception? deleteFailure)
        {
            if (closeFailure is not null && deleteFailure is not null)
            {
                throw new AggregateException(
                    "Workspace close and fixture deletion both failed.",
                    closeFailure,
                    deleteFailure);
            }

            var failure = closeFailure ?? deleteFailure;
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
