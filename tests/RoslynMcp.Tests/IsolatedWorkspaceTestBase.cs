using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

public abstract class IsolatedWorkspaceTestBase : TestBase
{
    protected static IsolatedWorkspaceScope CreateIsolatedWorkspaceCopy()
    {
        var solutionPath = CreateSampleSolutionCopy();
        var rootPath = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Isolated workspace root could not be resolved.");

        return new IsolatedWorkspaceScope(rootPath, solutionPath);
    }

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
        var execution = await commandRunner.RunAsync(
            workingDirectory: workspace.RootPath,
            targetPath: workspace.SolutionPath,
            arguments: ["restore", workspace.SolutionPath, "--nologo"],
            ct).ConfigureAwait(false);

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
        private readonly Func<string, bool> _closeWorkspace;
        private readonly Action<string> _deleteRoot;
        private int _disposeState;
        private string? _workspaceId;

        internal IsolatedWorkspaceScope(string rootPath, string solutionPath)
            : this(
                rootPath,
                solutionPath,
                workspaceId: null,
                WorkspaceManager.Close,
                DeleteDirectoryIfExists)
        {
        }

        internal IsolatedWorkspaceScope(
            string rootPath,
            string solutionPath,
            string? workspaceId,
            Func<string, bool> closeWorkspace,
            Action<string> deleteRoot)
        {
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
                var status = await WorkspaceManager.LoadAsync(SolutionPath, ct).ConfigureAwait(false);
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

            await WorkspaceManager.ReloadAsync(_workspaceId, ct).ConfigureAwait(false);
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
