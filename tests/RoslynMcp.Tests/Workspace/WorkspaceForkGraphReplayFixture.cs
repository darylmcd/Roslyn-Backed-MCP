using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

public abstract class WorkspaceForkGraphReplayFixture : IsolatedWorkspaceTestBase
{
    private protected static void WriteSafeGraphDiagnostics(Exception failure)
    {
        foreach (var key in new[] { "SdkGeneratedConfig", "SdkGeneratedConfigPath", "DifferentLineIndices", "RejectedFileName", "SameAfterDirectoryNormalization", "InsideOwnedRoot", "MetadataReferenceCounts", "MissingMetadataFileNames", "ExtraMetadataFileNames", "MissingAnalyzerFileNames", "ExtraAnalyzerFileNames", "ExecutionEntryCounts", "ExecutionNodeCounts", "ExecutionIdentityMismatchComponents", "ExecutionContextMismatchComponents" })
            if (failure.Data.Contains(key)) Console.WriteLine(key + ": " + failure.Data[key]);
    }

    private protected static async Task PublishPreviousExternalUndoAsync(IWorkspaceManager manager, IUndoService undo, string workspaceId, string path, string description)
    {
        var authority = (IWorkspaceRecoveryAuthority)manager;
        FileSnapshotDto[] files = [new FileSnapshotDto(path, null)];
        var physical = await authority.CaptureRecoveryAsync(workspaceId, description, files, CancellationToken.None);
        var operation = await undo.PrepareBeforeApplyAsync(physical, CancellationToken.None);
        var lease = await authority.BeginMutationAsync(physical, CancellationToken.None);
        Exception? primary = null;
        try
        {
            lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(path, Encoding.UTF8.GetBytes("// previous real file mutation")));
            await File.WriteAllTextAsync(path, "// previous real file mutation");
            lease.Complete([FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path))]);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Previous external undo lease cleanup failed.", primary,
                CleanupFailureCollector.FromAction(lease.Dispose));
        }
        await operation.PublishAsync(CancellationToken.None);
    }

    private protected static FailClosedWorkspaceManagerStub UndoFacade(
        Func<string, IReadOnlyList<string>, CancellationToken, Task> authorize,
        Func<PhysicalMutationRecovery, IReadOnlyList<FileSnapshotDto>, PhysicalGraphLayout?, CancellationToken, Task<IPhysicalRestorationAuthority>> stage,
        Func<PhysicalMutationRecovery, PhysicalGraphExpectation, bool, CancellationToken, Task> commit)
    {
        return FailClosedWorkspaceManagerStub.ForRecovery(WorkspaceManager, authorize,
            async (operation, files, layout, ct) =>
            {
                try { return await stage(operation, files, layout, ct); }
                catch (Exception failure)
                {
                    Console.WriteLine("Controlled sample Stage failure: " + failure.GetType().Name + ": " + failure.Message);
                    WriteSafeGraphDiagnostics(failure);
                    throw;
                }
            },
            async (operation, expected, retained, ct) =>
            {
                try { await commit(operation, expected, retained, ct); }
                catch (Exception failure)
                {
                    Console.WriteLine("Controlled Commit restoration failure: " + failure.GetType().Name + ": " + failure.Message);
                    WriteSafeGraphDiagnostics(failure);
                    throw;
                }
            });
    }

    private protected sealed class FailingRestorationCleanup(IPhysicalRestorationAuthority inner) : IPhysicalRestorationAuthority
    {
        public PhysicalGraphExpectation Expected => inner.Expected;
        public bool PreservesOriginalIdentity => inner.PreservesOriginalIdentity;
        public Task AssertSourceBeforeEffectAsync(PhysicalMutationRecovery operation, IReadOnlyList<string> sourceCoordinates, CancellationToken ct)
            => inner.AssertSourceBeforeEffectAsync(operation, sourceCoordinates, ct);
        public void Dispose()
        {
            inner.Dispose();
            throw new IOException("Synthetic stage disposal failure.");
        }
    }

    private protected sealed class NarrowedRequestPolicy : IWorkspacePathPolicy
    {
        internal const string Refusal = "Synthetic current request excludes captured target.";
        internal string? DeniedPath { get; set; }
        internal int Refusals { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (DeniedPath is { } denied && paths.Contains(denied, FileSystemPath.Comparer))
            {
                Refusals++;
                throw new UnauthorizedAccessException(Refusal);
            }
            return new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }

    private protected sealed class InterleavingDocumentFileSystem(Action<string> beforeWrite) : IDocumentSetFileSystem
    {
        public async Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct)
            => FileContentDigest.FromBytes(await ReadAllBytesAsync(path, ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The required file reader returned no content.");

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => File.ReadAllBytesAsync(path, ct);
        public Task TransitionAsync(string path, byte[]? expected, byte[]? desired, PhysicalMutationRecovery operation,
            IWorkspaceRecoveryAuthority authority, CancellationToken ct)
        {
            beforeWrite(path);
            return operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, null, ct);
        }
    }

    private protected static async Task<string> PrepareNativeMultiTargetProjectAsync(IsolatedWorkspaceScope source)
    {
        var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var framework = xml.Descendants("TargetFramework").Single();
        framework.Name = "TargetFrameworks";
        framework.Value = "net10.0;net10.0-windows";
        framework.AddAfterSelf(new XElement("TargetFramework", string.Empty));
        await File.WriteAllTextAsync(projectPath, xml.ToString(SaveOptions.DisableFormatting));
        var restored = await new DotnetCommandRunner().RunAsync(Path.GetDirectoryName(projectPath)!, projectPath,
            ["restore", "--verbosity", "quiet"], CancellationToken.None);
        Assert.AreEqual(0, restored.ExitCode, "Both target-framework assets must exist before proving the native graph.");
        return projectPath;
    }

    private protected sealed class ForkExecutionMutationRunner(bool createNearerConfiguration) : IDotnetCommandRunner
    {
        internal int Mutations { get; private set; }
        internal string? MutatedPath { get; private set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct) =>
            RunAsync(workingDirectory, targetPath, arguments, null, ct);
        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            var execution = await new DotnetCommandRunner().RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns, ct);
            Assert.IsTrue(execution.Succeeded, "The real restore prerequisite must complete before the controlled authored-input mutation.");
            if (Mutations == 0 && arguments.Contains("restore"))
            {
                Assert.IsTrue(targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
                MutatedPath = createNearerConfiguration ? Path.Combine(Path.GetDirectoryName(targetPath)!, "global.json") : targetPath;
                if (createNearerConfiguration)
                {
                    Assert.IsFalse(File.Exists(MutatedPath));
                    await File.WriteAllTextAsync(MutatedPath, "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}", ct);
                }
                else
                {
                    var xml = XDocument.Load(targetPath, LoadOptions.PreserveWhitespace);
                    xml.Root!.Add(new XElement("PropertyGroup", new XElement("VSTestTestCaseFilter", "NeverReviewedExecution")));
                    await File.WriteAllTextAsync(targetPath, xml.ToString(SaveOptions.DisableFormatting), ct);
                }
                Mutations++;
            }
            return execution;
        }
    }

    private protected sealed class InventoryPolicy : IWorkspacePathPolicy
    {
        internal List<string> Paths { get; } = [];
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            Paths.AddRange(paths);
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }

    private protected sealed class WriteThenFailRollbackFileSystem(string failingPath) : IDocumentSetFileSystem
    {
        public async Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct)
            => FileContentDigest.FromBytes(await ReadAllBytesAsync(path, ct).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The required file reader returned no content.");

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => File.ReadAllBytesAsync(path, ct);
        public async Task TransitionAsync(string path, byte[]? expected, byte[]? desired, PhysicalMutationRecovery operation,
            IWorkspaceRecoveryAuthority authority, CancellationToken ct)
        {
            if (desired is not null && FileSystemPath.Comparer.Equals(path, failingPath))
            {
                var currentBytes = await File.ReadAllBytesAsync(path, ct);
                if (desired.AsSpan().SequenceEqual(currentBytes))
                    throw new IOException("Injected rollback write failure.");
                if (System.Text.Encoding.UTF8.GetString(desired).Contains("// failed physical write", StringComparison.Ordinal))
                    await operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, null, ct);
                throw new IOException("Injected write failure after effects or during rollback.");
            }
            await operation.TransitionSourceAsync(path, expected, desired, PhysicalSourceTransitionMode.PreserveExistingObject, authority, null, ct);
        }
    }

    private protected sealed class ControlledReloadLoader : WorkspaceSessionLoader
    {
        internal bool RejectReload { get; set; }
        public override Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease, NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(
            string workspaceId, string path, IDictionary<string, string>? globalProperties,
            IWorkspacePathPolicy pathPolicy, WorkspaceDiagnosticsSink diagnostics, ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct) =>
            RejectReload ? throw InvalidOperationErrors.Internal("Injected candidate load rejection.")
                : base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger, supplementalPackages, sourceAdmission, ct);
    }

    private protected static async Task<IsolatedWorkspaceScope> CreateInteropWorkspaceAsync()
    {
        var root = Path.Combine(TestTempRoot.Current, "InteropRecovery", Guid.NewGuid().ToString("N"));
        var source = CreateWorkspaceScope(root, Path.Combine(root, "InteropRecovery.slnx"), WorkspaceManager);
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(source.GetPath("global.json"), "{}");
            await File.WriteAllTextAsync(source.GetPath("Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(source.SolutionPath,
                """<Solution><Project Path="SampleApp/SampleApp.csproj" /><Project Path="SampleLib/SampleLib.csproj" /><Project Path="Interop/Interop.csproj" /></Solution>""");
            foreach (var name in new[] { "SampleApp", "SampleLib", "Interop" })
            {
                Directory.CreateDirectory(source.GetPath(name));
                await File.WriteAllTextAsync(source.GetPath(name, name + ".csproj"),
                    """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>""");
            }
            await File.WriteAllTextAsync(source.GetPath("SampleLib", "Library.cs"), "public class Library { }");
            await File.WriteAllTextAsync(source.GetPath("SampleApp", "App.cs"), "public class App : Library { }");
            await File.WriteAllTextAsync(source.GetPath("Interop", "Interop.cs"), """
                using System.Runtime.InteropServices;
                [assembly: Guid("AF9A8763-3C34-47BE-9812-15103054CFC0")]
                [assembly: ImportedFromTypeLib("TestInterop")]
                namespace TestInterop;
                [ComImport, Guid("C5041789-2378-47C6-9638-E60BA9868145"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface ITestInterop { void Invoke(); }
                """);
            var path = source.GetPath("SampleApp", "SampleApp.csproj");
            var xml = XDocument.Load(path);
            xml.Root!.Add(new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", "../SampleLib/SampleLib.csproj")),
                new XElement("ProjectReference", new XAttribute("Include", "../Interop/Interop.csproj"))));
            await File.WriteAllTextAsync(path, xml.ToString());
            await RestoreWorkspaceAsync(source);
            await source.LoadAsync();
            return source;
        }
        catch (Exception primary)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Interop fixture cleanup failed.", primary,
                () => source.DisposeAsync());
            throw;
        }
    }
}
