using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeBuildMembershipTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("Extra.cs", "public class Extra {}", true)]
    [DataRow("payload/new.txt", "payload", true)]
    [DataRow("inputs/new.json", "{}", true)]
    [DataRow("imports/new.props", "<Project />", true)]
    [DataRow("enable.marker", "enabled", true)]
    [DataRow("Excluded.cs", "public class Excluded {}", false)]
    [DataRow("Removed.cs", "public class Removed {}", false)]
    [DataRow("ignored.bin", "ignored", false)]
    [DataRow("conditional/new.props", "<Project />", false)]
    public async Task Certificate_ReevaluatesNativeMembershipIncludingPreviouslyEmptySelectors(string relative, string content, bool changesMembership)
    {
        await WithScopeAsync(async scope =>
        {
            var solution = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            var certificate = NativeCompilerContextCertificate.Require(solution);
            await certificate.AssertCurrentAsync(solution, scope.WorkspaceId, PathPolicy, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(solution.FilePath!)!, relative), content);
            if (changesMembership)
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    certificate.AssertCurrentAsync(solution, scope.WorkspaceId, PathPolicy, CancellationToken.None));
            else
                await certificate.AssertCurrentAsync(solution, scope.WorkspaceId, PathPolicy, CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task QueuedNativeTest_NewCompileMemberRefusesBeforeProcessStart()
    {
        await WithScopeAsync(async scope =>
        {
            var runner = new HoldingRunner();
            GatedCommandExecutor? executor = null;
            Task? blocker = null;
            Task? pending = null;
            var observed = false;
            var checks = 0;
            Exception? primary = null;
            try
            {
                var solution = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
                var certificate = NativeCompilerContextCertificate.Require(solution);
                await certificate.AssertCurrentAsync(solution, scope.WorkspaceId, PathPolicy, CancellationToken.None);
                var root = Path.GetDirectoryName(solution.FilePath!)!;
                var project = solution.Projects.Single().FilePath!;
                executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, NullLogger<GatedCommandExecutor>.Instance);
                blocker = executor.ExecuteAsync(scope.WorkspaceId, project, ["hold"], TimeSpan.FromSeconds(30), CancellationToken.None);
                await runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                pending = executor.ExecuteTestAsync(scope.WorkspaceId, project, ["test", "--no-restore"],
                    (_, _) => throw new AssertFailedException("A revoked invocation cannot reach result collection."),
                    TimeSpan.FromSeconds(30), null, token =>
                    {
                        checks++;
                        return certificate.AssertCurrentAsync(solution, scope.WorkspaceId, PathPolicy, token);
                    }, CancellationToken.None);
                Assert.AreEqual(0, checks);
                await File.WriteAllTextAsync(Path.Combine(root, "AddedWhileQueued.cs"), "public class AddedWhileQueued {}");
                runner.Release.TrySetResult();
                await blocker;
                observed = true;
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => pending);
                Assert.AreEqual(1, checks);
                Assert.AreEqual(1, runner.Calls);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                runner.Release.TrySetResult();
                await CleanupFailureCollector.RunAfterFailureAsync("Queued native membership cleanup failed.", primary,
                    async () => { if (blocker is not null) await blocker; },
                    async () => { if (pending is not null && !observed) await pending; },
                    CleanupFailureCollector.FromAction(() => executor?.Dispose()));
            }
        });
    }

    private static async Task WithScopeAsync(Func<IsolatedWorkspaceScope, Task> action)
    {
        IsolatedWorkspaceScope? scope = null;
        Exception? primary = null;
        try
        {
            scope = await CreateAsync();
            await action(scope);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native membership scope cleanup failed.", primary,
                () => scope is null ? ValueTask.CompletedTask : scope.DisposeAsync());
        }
    }

    private static async Task<IsolatedWorkspaceScope> CreateAsync()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeBuildMembershipTests), Guid.NewGuid().ToString("N"));
        var scope = CreateWorkspaceScope(root, Path.Combine(root, "Membership.slnx"), WorkspaceManager);
        try
        {
            foreach (var directory in new[] { root, Path.Combine(root, "inputs"), Path.Combine(root, "payload"), Path.Combine(root, "imports"), Path.Combine(root, "conditional") })
                Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(root, "Membership.slnx"), "<Solution><Project Path=\"Membership.csproj\" /></Solution>");
            await File.WriteAllTextAsync(Path.Combine(root, "Membership.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <PropertyGroup Condition="Exists('enable.marker')"><DefineConstants>$(DefineConstants);ENABLED_MEMBER</DefineConstants></PropertyGroup>
                  <ItemGroup>
                    <Compile Include="*.cs" Exclude="Excluded*.cs" /><Compile Remove="Removed*.cs" />
                    <AdditionalFiles Include="inputs/*.json" /><None Include="payload/*.txt" />
                  </ItemGroup>
                  <Import Project="imports/*.props" />
                  <Import Project="conditional/*.props" Condition="Exists('enable.marker')" />
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public class Entry {}");
            await RestoreWorkspaceAsync(scope);
            await scope.LoadAsync();
            return scope;
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native membership fixture setup cleanup failed.", failure, () => scope.DisposeAsync());
            throw;
        }
    }

    private sealed class HoldingRunner : IDotnetCommandRunner
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct) =>
            RunAsync(workingDirectory, targetPath, arguments, null, ct);
        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            Calls++;
            if (arguments[0] == "hold") { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return new("dotnet", arguments, workingDirectory, targetPath, 0, true, 1, "", "");
        }
    }
}
