using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphCleanupAuthorityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("normal")]
    [DataRow("root")]
    [DataRow("ancestor")]
    public async Task ForkFailureCleanup_PreservesOrdinaryReplacement(string replacement)
    {
        var root = Path.Combine(TestTempRoot.Current, "fork-cleanup-native-" + Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(root, "Cleanup.csproj");
        var codePath = Path.Combine(root, "Entry.cs");
        string? workspaceId = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(projectPath, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
            await File.WriteAllTextAsync(codePath, "public class Entry { public int Value => 1; }");
            var properties = OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string> { ["RestorePackagesPath"] = Path.Combine(root, "packages") });
            var restored = await DotnetCommandRunner.RunAsync(root, projectPath,
                MsBuildCommandProperties.Append(["restore", projectPath, "--nologo"], properties), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "The single-project native fixture restore must succeed.");
            var loaded = await ((IWorkspaceEvaluationLoader)WorkspaceManager).LoadWithPropertiesAsync(projectPath, properties, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            workspaceId = loaded.WorkspaceId;
            var original = WorkspaceManager.GetCurrentSolution(workspaceId);
            Assert.AreEqual(1, original.ProjectIds.Count, "Cleanup must exercise a real one-project native graph.");
            var document = original.Projects.Single().Documents.Single(document => FileSystemPath.Comparer.Equals(document.FilePath, codePath));
            var modified = original.WithDocumentText(document.Id, SourceText.From("public class Entry { public int Value => 2; }"));
            var sourceBytes = new Dictionary<string, byte[]>();
            foreach (var path in new[] { projectPath, codePath, Path.Combine(root, "global.json"), Path.Combine(root, "Directory.Build.props"), Path.Combine(root, "obj", "project.assets.json") })
                sourceBytes.Add(path, await File.ReadAllBytesAsync(path));
            var version = WorkspaceManager.GetCurrentVersion(workspaceId);
            var token = PreviewStore.Store(workspaceId, original, modified, version,
                "exercise issued fork cleanup", [], PreviewKind.Unspecified);
            var runner = new ReplacingFailedRestoreRunner(root, replacement);
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker);
            Directory.CreateDirectory(root + "-private");
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager,
                WorkspaceExecutionGate, PreviewStore, validation, TestRunnerService, runner,
                NullLogger<WorkspaceForkApplyService>.Instance, Path.Combine(root + "-private", "roslynmcp-forks"), TimeProvider.System);

            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(workspaceId,
                token, "drop-always", false, null, "cleanup-authority", CancellationToken.None));

            StringAssert.Contains(failure.Message, "dotnet restore");
            Assert.IsTrue(runner.Invoked, "The actual fork must reach its restore command before replacement.");
            Assert.IsNotNull(runner.IssuedPath);
            if (runner.ReplacementMarker is { } marker)
            {
                Assert.IsTrue(File.Exists(marker), "Failure cleanup must preserve the unrelated ordinary replacement.");
                Assert.AreEqual("unrelated replacement bytes", await File.ReadAllTextAsync(marker));
                Assert.IsFalse(File.Exists(Path.Combine(runner.OriginalPath!, "owned-cleanup-input.txt")),
                    "Cleanup must remove known bytes through its original owned directory identity.");
            }
            else if (OperatingSystem.IsWindows())
                Assert.IsFalse(Directory.Exists(runner.IssuedPath), "Windows must remove the issued owned directory namespace.");
            else
            {
                Assert.IsTrue(Directory.Exists(runner.IssuedPath), "POSIX must retain the final directory names.");
                Assert.IsFalse(Directory.EnumerateFiles(runner.IssuedPath, "*", SearchOption.AllDirectories).Any());
                var outcomes = Assert.IsInstanceOfType<string[]>(failure.Data[OwnedResourceCleanup.OutcomeDataKey]);
                Assert.IsTrue(outcomes.Any(outcome => outcome.Contains("directory names were retained", StringComparison.Ordinal)),
                    "Failure cleanup must report retained directory names separately from the restore failure.");
            }
            foreach (var (path, bytes) in sourceBytes) CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspaceId));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspaceId));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native cleanup fixture release failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root + "-private")));
        }
    }

    private sealed class ReplacingFailedRestoreRunner(string sourceRoot, string replacement) : IDotnetCommandRunner
    {
        internal bool Invoked { get; private set; }
        internal string? IssuedPath { get; private set; }
        internal string? OriginalPath { get; private set; }
        internal string? ReplacementMarker { get; private set; }

        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct) =>
            RunAsync(workingDirectory, targetPath, arguments, ct);

        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Invoked = true;
            var issued = Path.GetDirectoryName(targetPath);
            while (issued is not null && Path.GetFileName(Path.GetDirectoryName(issued)) != "roslynmcp-forks")
                issued = Path.GetDirectoryName(issued);
            Assert.IsNotNull(issued, "The command must target the actual isolated fork tree.");
            IssuedPath = issued;
            File.WriteAllText(Path.Combine(issued, "owned-cleanup-input.txt"), "owned fork bytes");
            var retained = Path.Combine(sourceRoot, "retained-fork");
            var moved = false;
            try
            {
                if (replacement != "normal")
                {
                    Directory.Move(replacement == "ancestor" ? Path.GetDirectoryName(issued)! : issued, retained);
                    moved = true;
                }
                else OriginalPath = issued;
            }
            catch (IOException) when (OperatingSystem.IsWindows()) { OriginalPath = issued; }
            if (moved)
            {
                OriginalPath = replacement == "ancestor" ? Path.Combine(retained, Path.GetFileName(issued)) : retained;
                Directory.CreateDirectory(issued);
                ReplacementMarker = Path.Combine(issued, "unrelated-replacement.txt");
                File.WriteAllText(ReplacementMarker, "unrelated replacement bytes");
                Assert.IsFalse((File.GetAttributes(issued) & FileAttributes.ReparsePoint) != 0);
            }
            return Task.FromResult(new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath,
                1, false, 0, string.Empty, "Synthetic restore refusal after the owned directory replacement attempt."));
        }
    }
}
