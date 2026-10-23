using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed partial class HostRestoreAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task RecordedCustomDestination_RequiresCurrentGrantBeforeRestore(bool allowed) =>
        WithFixtureAsync(false, async fixture =>
        {
            fixture.DestinationAllowed = allowed;
            using var executor = fixture.CreateExecutor();
            if (allowed)
            {
                await fixture.RestoreAsync(executor);
                Assert.AreEqual(1, fixture.Runner.RestoreCalls);
                Assert.IsGreaterThan(0, fixture.DestinationChecks, "The recorded --packages destination needs an explicit current grant.");
                CollectionAssert.Contains(fixture.Runner.Arguments.Single().ToArray(), fixture.Cache);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => fixture.RestoreAsync(executor));
                Assert.AreEqual(0, fixture.Runner.RestoreCalls);
                Assert.IsFalse(File.Exists(fixture.Effect));
            }
        });

    [TestMethod]
    [DataRow("grant")]
    [DataRow("assets")]
    [DataRow("definition")]
    public Task QueuedHostRestore_RevalidatesDestinationAndSelectingInputsBeforeProcess(string mutation) =>
        WithFixtureAsync(false, fixture => AssertQueuedRestoreRefusalAsync(fixture,
            async () =>
            {
                if (mutation == "grant") fixture.DestinationAllowed = false;
                else if (mutation == "assets") await fixture.WriteAssetsAsync(fixture.OwnerPath, fixture.Cache + "-changed");
                else await File.WriteAllTextAsync(fixture.ImportPath, "<Project><PropertyGroup><Frozen>changed</Frozen></PropertyGroup></Project>");
            }, pending => mutation == "grant"
                ? Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => pending)
                : Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => pending)));

    [TestMethod]
    public Task QueuedHostRestore_RejectsSelectorMutationDuringFinalMembershipRecheck() =>
        WithFixtureAsync(false, async fixture =>
        {
            var definition = XDocument.Load(fixture.OwnerPath);
            definition.Root!.Element("PropertyGroup")!.Add(new XElement("MSBuildProjectExtensionsPath", Path.Combine(fixture.Root, "obj")));
            await File.WriteAllTextAsync(fixture.OwnerPath, definition.ToString());
            var authorize = fixture.AuthorizePaths;
            var armed = false;
            var epochChecked = false;
            var mutated = false;
            var assets = fixture.Assets(fixture.OwnerPath);
            fixture.AuthorizePaths = async (paths, token) =>
            {
                await authorize(paths, token);
                if (!armed) return;
                if (paths.Count > 1 && paths.Contains(assets, FileSystemPath.Comparer)) epochChecked = true;
                if (epochChecked && !mutated && paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], assets))
                {
                    mutated = true;
                    await fixture.WriteAssetsAsync(fixture.OwnerPath, fixture.Cache, "changed-during-membership");
                }
            };
            await AssertQueuedRestoreRefusalAsync(fixture, () => { armed = true; return Task.CompletedTask; },
                async pending =>
                {
                    Exception? refusal = null;
                    try { await pending; }
                    catch (Exception failure) { refusal = failure; }
                    Assert.IsTrue(mutated, "The selecting asset must change after its entry epoch check, during graph/package reconstruction.");
                    Assert.AreEqual(typeof(PublicInvalidOperationException), refusal?.GetType());
                });
        });

    private static async Task AssertQueuedRestoreRefusalAsync(Fixture fixture, Func<Task> whileQueued, Func<Task, Task> assertRefusal)
    {
        using var observed = new QueuedRestoreExecutor(fixture.CreateExecutor());
        var blocker = observed.ExecuteAsync(fixture.Status.WorkspaceId, fixture.OwnerPath, ["hold"], TimeSpan.FromSeconds(30), CancellationToken.None);
        Task? pending = null;
        var pendingObserved = false;
        Exception? primary = null;
        try
        {
            await fixture.Runner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pending = fixture.RestoreAsync(observed);
            await observed.Queued.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(pending.IsCompleted, "The actual command gate must hold the fully prepared restore.");
            Assert.AreEqual(0, fixture.Runner.RestoreCalls);
            await whileQueued();
            fixture.Runner.Release.TrySetResult();
            await blocker;
            pendingObserved = true;
            await assertRefusal(pending);
            Assert.AreEqual(0, fixture.Runner.RestoreCalls);
            Assert.IsFalse(File.Exists(fixture.Effect));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Queued host restore fixture failed to drain.", primary,
                async () => { await blocker; },
                async () => { if (pending is not null && !pendingObserved) await pending; });
        }
    }

    [TestMethod]
    [DataRow("assets")]
    [DataRow("definition")]
    [DataRow("grant")]
    [DataRow("membership")]
    public Task QueuedHostRestore_ClosesInputsAndGrantAfterDestinationCallbacks(string mutation) =>
        WithFixtureAsync(false, async fixture =>
        {
            if (mutation == "membership")
            {
                var definition = XDocument.Load(fixture.OwnerPath);
                definition.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "*.cs"))));
                await File.WriteAllTextAsync(fixture.OwnerPath, definition.ToString());
            }
            var original = fixture.AuthorizePaths;
            var armed = false;
            var destinationChecked = false;
            var mutated = false;
            fixture.AuthorizePaths = async (paths, token) =>
            {
                await original(paths, token);
                if (!armed || mutated) return;
                if (paths.Contains(fixture.Cache, FileSystemPath.Comparer)) destinationChecked = true;
                if (!destinationChecked || mutation == "grant" && paths.Count != 0) return;
                mutated = true;
                if (mutation == "assets") await fixture.WriteAssetsAsync(fixture.OwnerPath, fixture.Cache, "after-input-proof");
                else if (mutation == "definition") await File.WriteAllTextAsync(fixture.ImportPath, "<Project><PropertyGroup><Frozen>after-input-proof</Frozen></PropertyGroup></Project>");
                else if (mutation == "membership") await File.WriteAllTextAsync(Path.Combine(fixture.Root, "NewMatchingInput.cs"), "public class NewMatchingInput { }");
                else fixture.DestinationAllowed = false;
            };
            await AssertQueuedRestoreRefusalAsync(fixture, () => { armed = true; return Task.CompletedTask; }, async pending =>
            {
                Exception? refusal = null;
                try { await pending; }
                catch (Exception failure) { refusal = failure; }
                Assert.IsTrue(mutated, "The actual final destination policy callback must mutate state after the input proof.");
                Assert.AreEqual(mutation == "grant" ? typeof(UnauthorizedAccessException) : typeof(PublicInvalidOperationException), refusal?.GetType());
            });
        });

    [TestMethod]
    [DataRow("property", false)]
    [DataRow("property", true)]
    public Task MissingAssets_EffectiveNativeDestinationNeedsGrantWithoutInventingPackagesArgument(string source, bool allowed) =>
        WithFixtureAsync(false, async fixture =>
        {
            var definition = XDocument.Load(fixture.OwnerPath);
            definition.Root!.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
            var group = definition.Root.Element("PropertyGroup")!;
            group.Add(new XElement("NuGetAudit", "false"));
            if (source == "property") group.Add(new XElement("RestorePackagesPath", fixture.Cache));
            definition.Save(fixture.OwnerPath);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "NuGet.Config"), new XElement("configuration",
                new XElement("packageSources", new XElement("clear")),
                new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", fixture.Cache)))).ToString());
            File.Delete(fixture.Assets(fixture.OwnerPath));
            // Execute the real SDK settings target, without restore or package downloads.
            var native = await new DotnetCommandRunner().RunAsync(fixture.Root, fixture.OwnerPath,
                ["msbuild", fixture.OwnerPath, "-nologo", "-verbosity:quiet", "-nodeReuse:false", "-maxCpuCount:1",
                    "-target:_GetRestoreSettings", "-getProperty:_OutputPackagesPath,MSBuildProjectFullPath"], CancellationToken.None);
            Assert.IsTrue(native.Succeeded, native.StdErr + native.StdOut);
            using var values = JsonDocument.Parse(native.StdOut);
            var actualDestination = values.RootElement.GetProperty("Properties").GetProperty("_OutputPackagesPath").GetString();
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(fixture.Cache), Path.TrimEndingDirectorySeparator(actualDestination!),
                "Actual native restore settings must select the custom destination before testing its authority.");
            fixture.DestinationAllowed = allowed;
            using var executor = fixture.CreateExecutor();
            if (allowed)
            {
                await fixture.RestoreAsync(executor);
                Assert.AreEqual(1, fixture.Runner.RestoreCalls);
                Assert.IsGreaterThan(0, fixture.DestinationChecks);
                Assert.IsFalse(fixture.Runner.Arguments.Single().Contains("--packages"), "Missing recorded root preserves configured/default CLI semantics.");
            }
            else
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => fixture.RestoreAsync(executor));
                Assert.AreEqual(0, fixture.Runner.RestoreCalls);
                Assert.IsFalse(File.Exists(fixture.Effect));
            }
        });

    [TestMethod]
    public Task ConfiguredDestination_UsesNativeSettingsWithoutInheritedEnvironmentOverride() =>
        NativeConditionProcessTest.RunAsync(typeof(HostRestoreAuthorityTests),
            nameof(ConfiguredDestination_UsesNativeSettingsWithoutInheritedEnvironmentOverride),
            new Dictionary<string, string?> { ["NUGET_PACKAGES"] = null }, async () =>
            {
                await MissingAssets_EffectiveNativeDestinationNeedsGrantWithoutInventingPackagesArgument("config", false);
                await MissingAssets_EffectiveNativeDestinationNeedsGrantWithoutInventingPackagesArgument("config", true);
            });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NativeConfigSelection_UsesExactHostPolicyInputsWithoutAncestorDirectoryGrants(bool denyAncestorCandidate) =>
        WithFixtureAsync(false, async fixture =>
        {
            var definition = XDocument.Load(fixture.OwnerPath);
            definition.Root!.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
            definition.Root.Element("PropertyGroup")!.Add(new XElement("RestorePackagesPath", fixture.Cache), new XElement("NuGetAudit", "false"));
            definition.Save(fixture.OwnerPath);
            File.Delete(fixture.Assets(fixture.OwnerPath));
            var graph = EvaluatedBuildGraph.Source(fixture.Solution, CancellationToken.None);
            var node = graph.Nodes.Single(item => !item.IsOuterBuild);
            var (discovered, _) = await NativeCompilerTaskInputs.ReadRawAsync(node.ProjectPath, node.Globals, node.TargetFramework,
                node.TaskRegistrations.ToolsVersion, ["_GetRestoreSettings"], NativeCompilerTaskInputs.CapturePurpose.RestoreSettings, CancellationToken.None);
            var settings = discovered.RestoreSettings.Single();
            var files = settings.Parameters.GetValueOrDefault("OutputConfigFilePaths")!.ToHashSet(FileSystemPath.Comparer);
            // Independently selected native user-extension probes need exact current
            // grants too; SDK authorship does not grant their external parent trees.
            var sdk = BuildInputProvenance.RegisteredSdkDirectory();
            files.UnionWith(graph.Nodes.SelectMany(item => item.PathEvidence.ConditionProbes)
                .Where(probe => FileSystemPath.IsStrictDescendant(sdk, probe.Origin)
                    && Path.GetFileName(Path.TrimEndingDirectorySeparator(probe.Path)) is "ImportBefore" or "ImportAfter")
                .Select(probe => probe.Path));
            var ancestorDirectories = new HashSet<string>(FileSystemPath.Comparer);
            for (var directory = Path.GetDirectoryName(fixture.Root); directory is not null; directory = Path.GetDirectoryName(directory))
            {
                ancestorDirectories.Add(directory);
                files.Add(Path.Combine(directory, ".editorconfig"));
                // Grant exact supported filenames, never a parent tree. The real SDK
                // oracle above establishes selected files independently of the renderer.
                foreach (var name in new[] { "NuGet.Config", "NuGet.config", "nuget.config" }) files.Add(Path.Combine(directory, name));
            }
            var denied = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "NuGet.Config");
            if (denyAncestorCandidate) files.RemoveWhere(path => FileSystemPath.Comparer.Equals(path, denied));
            var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = files.Append(fixture.Root).ToArray() });
            var refused = new List<string>();
            fixture.AuthorizePaths = async (paths, token) =>
            {
                Assert.IsFalse(paths.Any(path => ancestorDirectories.Contains(Path.TrimEndingDirectorySeparator(path))),
                    "Native configuration selection does not grant or enumerate any ancestor directory.");
                try { await policy.ValidateAsync(fixture.Status.WorkspaceId, paths, token); }
                catch (Exception failure) when (WorkspacePathPolicyRefusal.IsDenied(failure))
                {
                    refused.AddRange(paths.Where(path => !files.Contains(path) && !FileSystemPath.IsStrictDescendant(fixture.Root, path)));
                    throw;
                }
            };
            using var executor = fixture.CreateExecutor();
            if (denyAncestorCandidate)
            {
                var failure = await Assert.ThrowsAsync<ArgumentException>(() => fixture.RestoreAsync(executor));
                Assert.IsTrue(WorkspacePathPolicyRefusal.IsDenied(failure));
                Assert.IsTrue(refused.Contains(denied, FileSystemPath.Comparer), string.Join(",", refused.Select(Path.GetFileName)));
                Assert.AreEqual(0, fixture.Runner.RestoreCalls);
                Assert.IsFalse(File.Exists(fixture.Effect));
            }
            else
            {
                await fixture.RestoreAsync(executor);
                Assert.AreEqual(1, fixture.Runner.RestoreCalls);
                Assert.HasCount(0, refused);
            }
        });

    [TestMethod]
    [DataRow(";")]
    [DataRow("%3B")]
    public Task NativeSettingsPropertyOutputs_DecodeArraysAndLiteralEscapesExactlyOnce(string literal) =>
        WithFixtureAsync(false, async fixture =>
        {
            var configuration = Path.Combine(fixture.Root, "NuGet" + literal + "Literal.Config");
            var cache = Path.Combine(fixture.Root, "cache" + literal + "literal");
            await File.WriteAllTextAsync(configuration, "<configuration><packageSources><clear /></packageSources></configuration>");
            var definition = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("RestoreConfigFile", Microsoft.Build.Evaluation.ProjectCollection.Escape(configuration)),
                    new XElement("RestorePackagesPath", Microsoft.Build.Evaluation.ProjectCollection.Escape(cache))));
            await NativeSdkRestoreFixture.WriteProjectAsync(fixture.OwnerPath, definition.ToString(), CancellationToken.None);
            var node = EvaluatedBuildGraph.Source(fixture.Solution, CancellationToken.None).Nodes.Single(item => !item.IsOuterBuild);
            var (capture, _) = await NativeCompilerTaskInputs.ReadRawAsync(node.ProjectPath, node.Globals, node.TargetFramework,
                node.TaskRegistrations.ToolsVersion, ["_GetRestoreSettings"], NativeCompilerTaskInputs.CapturePurpose.RestoreSettings, CancellationToken.None);
            var settings = capture.RestoreSettings.Single();
            CollectionAssert.Contains(settings.Parameters["OutputConfigFilePaths"], configuration);
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(cache), Path.TrimEndingDirectorySeparator(settings.Parameters["OutputPackagesPath"].Single()));
            Assert.IsTrue(settings.Parameters["OutputConfigFilePaths"].All(Path.IsPathFullyQualified));
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task MultiNodeRestore_AdvancesOnlySuccessfulGeneratedOutputEpoch(bool mutateAuthoredInput) =>
        WithFixtureAsync(true, async fixture =>
        {
            fixture.Runner.AfterRestore = async call =>
            {
                if (call != 1) return;
                await fixture.WriteAssetsAsync(fixture.DependencyPath!, fixture.Cache, "produced");
                if (mutateAuthoredInput)
                    await File.WriteAllTextAsync(fixture.ImportPath, "<Project><PropertyGroup><Frozen>changed</Frozen></PropertyGroup></Project>");
            };
            using var executor = fixture.CreateExecutor();
            if (mutateAuthoredInput)
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.RestoreAsync(executor));
                Assert.AreEqual(1, fixture.Runner.RestoreCalls, "The first successful restore cannot recertify changed authored inputs for the next node.");
            }
            else
            {
                await fixture.RestoreAsync(executor);
                Assert.AreEqual(2, fixture.Runner.RestoreCalls);
                CollectionAssert.AreEqual(new[] { fixture.DependencyPath!, fixture.OwnerPath }, fixture.Runner.Targets.ToArray());
            }
        });

    private static async Task WithFixtureAsync(bool dependency, Func<Fixture, Task> inspect)
    {
        MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, nameof(HostRestoreAuthorityTests), Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            workspace = new AdhocWorkspace();
            var fixture = new Fixture(root, workspace);
            await fixture.InitializeAsync(dependency);
            await inspect(fixture);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Host restore fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class Fixture
    {
        internal Fixture(string root, AdhocWorkspace workspace)
        {
            Root = root;
            Solution = workspace.CurrentSolution;
            AuthorizePaths = AuthorizeDestinationAsync;
        }
        internal string Root { get; }
        internal Func<IReadOnlyList<string>, CancellationToken, Task> AuthorizePaths { get; set; }
        internal string OwnerPath => Path.Combine(Root, "Owner.csproj");
        internal string ImportPath => Path.Combine(Root, "Frozen.props");
        internal string Cache => Path.Combine(Root, "external-cache");
        internal string Effect => Path.Combine(Root, "obj", "restore-started.marker");
        internal string? DependencyPath { get; private set; }
        internal Solution Solution { get; private set; }
        internal WorkspaceStatusDto Status { get; private set; } = null!;
        internal FailClosedWorkspaceManagerStub Manager { get; private set; } = null!;
        internal RestoreRunner Runner { get; private set; } = null!;
        internal bool DestinationAllowed { get; set; } = true;
        internal int DestinationChecks { get; private set; }
        internal int ReloadCalls { get; private set; }

        internal async Task InitializeAsync(bool dependency)
        {
            NativeSdkRestoreFixture.InitializeDirectory(Root);
            await File.WriteAllTextAsync(ImportPath, "<Project><PropertyGroup><Frozen>before</Frozen></PropertyGroup></Project>");
            var owner = ProjectId.CreateNewId();
            Solution = Solution.AddProject(ProjectInfo.Create(owner, VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: OwnerPath));
            var definition = new XElement("Project", new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("ProjectAssetsFile", Assets(OwnerPath))), new XElement("Import", new XAttribute("Project", "Frozen.props")));
            if (dependency)
            {
                DependencyPath = Path.Combine(Root, "Dependency.csproj");
                await File.WriteAllTextAsync(DependencyPath, new XElement("Project", new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("ProjectAssetsFile", Assets(DependencyPath)))).ToString());
                var peer = ProjectId.CreateNewId();
                Solution = Solution.AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Dependency", "Dependency", LanguageNames.CSharp, filePath: DependencyPath))
                    .AddProjectReference(owner, new ProjectReference(peer));
                definition.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "Dependency.csproj"))));
                await WriteAssetsAsync(DependencyPath, Cache);
            }
            await File.WriteAllTextAsync(OwnerPath, definition.ToString());
            await WriteAssetsAsync(OwnerPath, Cache);
            Status = new WorkspaceStatusDto("host-restore", OwnerPath, 1, "host-restore:1", DateTimeOffset.UtcNow, Solution.ProjectIds.Count,
                0, [], true, false, [], RestoreRequired: true);
            Manager = new FailClosedWorkspaceManagerStub(Status)
            {
                GetCurrentSolutionHandler = _ => Solution,
                GetCurrentVersionHandler = _ => 1,
                GetStatusHandler = _ => Status,
                ReloadHandler = (_, _) => { ReloadCalls++; return Task.FromResult(Status); },
                AuthorizePathsHandler = (_, paths, ct) => AuthorizePaths(paths, ct)
            };
            Runner = new RestoreRunner(Effect);
        }

        private Task AuthorizeDestinationAsync(IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Any(path => FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(path), Cache)))
            {
                DestinationChecks++;
                if (!DestinationAllowed) throw new UnauthorizedAccessException("Controlled custom destination denial.");
            }
            return Task.CompletedTask;
        }

        internal string Assets(string project) => Path.Combine(Root, Path.GetFileNameWithoutExtension(project) + ".assets.json");
        internal async Task WriteAssetsAsync(string project, string cache, string epoch = "entry") =>
            await File.WriteAllTextAsync(Assets(project), JsonSerializer.Serialize(new { project = new { restore = new { packagesPath = cache } }, epoch }));
        internal GatedCommandExecutor CreateExecutor() => new(Manager, Runner, NullLogger<GatedCommandExecutor>.Instance);
        internal Task<WorkspaceTools.RestoreReloadOutcome> RestoreAsync(IGatedCommandExecutor executor) =>
            WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(30) },
                Manager, Status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);
    }

    private sealed class QueuedRestoreExecutor(IGatedCommandExecutor inner) : IGatedCommandExecutor
    {
        internal TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CommandExecutionDto> ExecuteAuthorizedAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
        {
            var queued = inner.ExecuteAuthorizedAsync(workspaceId, targetPath, arguments, timeout, authorizeStart, ct);
            Queued.TrySetResult();
            return queued;
        }
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken ct) => inner.ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct);
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct) =>
            inner.ExecuteAsync(workspaceId, targetPath, arguments, timeout, earlyKillPatterns, ct);
        public Task<TestRunResultDto> ExecuteTestAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults, TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, Func<CancellationToken, Task> authorizeStart, CancellationToken ct) =>
            inner.ExecuteTestAsync(workspaceId, targetPath, arguments, collectResults, timeout, earlyKillPatterns, authorizeStart, ct);
        public Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct) => inner.ExecuteBuildAsync(plan, timeout, ct);
        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) => inner.ResolveProject(workspaceId, projectName);
        public void Dispose() => inner.Dispose();
    }

    private sealed class RestoreRunner(string effect) : CannedCommandRunner
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<IReadOnlyList<string>> Arguments { get; } = [];
        internal List<string> Targets { get; } = [];
        internal int RestoreCalls => Arguments.Count;
        internal Func<int, Task>? AfterRestore { get; set; }
        public override async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (arguments[0] == "hold")
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            else
            {
                Assert.AreEqual("restore", arguments[0]);
                Arguments.Add(arguments.ToArray());
                Targets.Add(targetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(effect)!);
                await File.WriteAllTextAsync(effect, "restore started", ct);
                if (AfterRestore is { } callback) await callback(RestoreCalls);
            }
            return new("dotnet", arguments, workingDirectory, targetPath, 0, true, 1, "", "");
        }
    }
}
