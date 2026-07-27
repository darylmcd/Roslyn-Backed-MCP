using System.Xml.Linq;

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
        await workspace.LoadAsync(ct).ConfigureAwait(false);
        return workspace;
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

    protected sealed class IsolatedWorkspaceScope : IAsyncDisposable, IDisposable
    {
        private bool _disposed;
        private string? _workspaceId;

        internal IsolatedWorkspaceScope(string rootPath, string solutionPath)
        {
            RootPath = rootPath;
            SolutionPath = solutionPath;
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_workspaceId is not null)
            {
                WorkspaceManager.Close(_workspaceId);
            }
            DeleteDirectoryIfExists(RootPath);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
