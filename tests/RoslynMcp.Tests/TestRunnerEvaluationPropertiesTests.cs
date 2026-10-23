using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed partial class TestRunnerEvaluationPropertiesTests
{
    private const string Sentinel = "synthetic-value%with;punctuation-671ba";
    private const string Encoded = "synthetic-value%25with%3Bpunctuation-671ba";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidNativeAssets_DoNotAuthorizeTestPreparationFromFreshSourceCapture(bool changeSource)
    {
        NuGetInstallationFixture fixture = await NuGetInstallationFixture.CreateAsync("Owned.UnavailableTestPreparation", "1.0.0",
            new Dictionary<string, byte[]> { ["marker.txt"] = "test dependency"u8.ToArray() });
        Exception? resourceCleanupFailure1 = null;
        try
        {
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var definition = System.Xml.Linq.XDocument.Load(project);
            definition.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("IsTestProject", "true")));
            await File.WriteAllTextAsync(project, definition.ToString(), TestContext.CancellationToken);
            var source = Path.Combine(fixture.Root, "Test.cs");
            await File.WriteAllTextAsync(source, "class Test { }", TestContext.CancellationToken);
            File.Delete(fixture.Assets);
            var policy = new ForkInvocationPathPolicy(FixturePolicy);
            var manager = CreateOwnedManager(policy);
            Exception? resourceCleanupFailure2 = null;
            try
            {
                WorkspaceStatusDto status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
                Assert.IsTrue(status.IsLoaded, "Missing package assets must preserve semantic loading.");
                Assert.IsTrue(NativeCompilerContextCertificate.TryGet(manager.GetCurrentSolution(status.WorkspaceId), out NativeCompilerContextCertificate? certificate));
                Assert.AreEqual(NativeCompilerContextAvailability.InvalidRestoreAssets, certificate!.Availability);
                if (changeSource) await File.WriteAllTextAsync(source, "class ChangedAfterLoad { }", TestContext.CancellationToken);
                var runner = new RecordingRunner
                {
                    RestoreSucceeds = true,
                    RestoreSideEffect = () => File.WriteAllText(fixture.Assets, "{\"libraries\":{}}")
                };
                var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
                Exception? resourceCleanupFailure3 = null;
                try
                {
                    var service = new TestRunnerService(policy, manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync(status.WorkspaceId, null, null, CancellationToken.None));
                    Assert.IsEmpty(runner.Commands, "An unavailable native load receipt cannot authorize restore or test execution, even with fresh current grants.");
                    Assert.IsFalse(File.Exists(fixture.Assets));

                }
                catch (Exception failure) { resourceCleanupFailure3 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure3, executor); }

            }
            catch (Exception failure) { resourceCleanupFailure2 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure2, manager); }

        }
        catch (Exception failure) { resourceCleanupFailure1 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure1, fixture); }
    }

    [TestMethod]
    [DataRow(false, "current")]
    [DataRow(true, "current")]
    [DataRow(false, "changed")]
    [DataRow(true, "changed")]
    [DataRow(false, "revoked")]
    [DataRow(true, "revoked")]
    public async Task CertifiedTestPreparation_ReusesOnlyCurrentAuthorizedRestoredInputs(bool native, string state)
    {
        NuGetInstallationFixture fixture = await NuGetInstallationFixture.CreateAsync("Owned.TestPreparation", "1.0.0",
            new Dictionary<string, byte[]> { ["marker.txt"] = "test dependency"u8.ToArray() });
        Exception? resourceCleanupFailure4 = null;
        try
        {
            var path = Path.Combine(fixture.Root, "Owner.csproj");
            var xml = System.Xml.Linq.XDocument.Load(path);
            xml.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("IsTestProject", "true"),
                new System.Xml.Linq.XElement("OutputType", "Exe"),
                new System.Xml.Linq.XElement("IsTestingPlatformApplication", native ? "true" : "false")));
            await File.WriteAllTextAsync(path, xml.ToString(), TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Program.cs"), "class Program { static void Main() { } }", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), native
                ? "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}" : "{}", TestContext.CancellationToken);
            var policy = new RevocableTestInputPolicy(fixture.Assets, FixturePolicy);
            var manager = CreateOwnedManager(new ForkInvocationPathPolicy(policy));
            Exception? resourceCleanupFailure5 = null;
            try
            {
                WorkspaceStatusDto status = await manager.LoadAsync(path, fixture.LoadProperties, CancellationToken.None);
                Solution solution = manager.GetCurrentSolution(status.WorkspaceId);
                _ = NativeCompilerContextCertificate.Require(solution);
                var before = await File.ReadAllBytesAsync(fixture.Assets, TestContext.CancellationToken);
                var runner = new RecordingRunner { RestoreSucceeds = true };
                var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
                Exception? resourceCleanupFailure6 = null;
                try
                {
                    var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                        NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                    if (state == "changed") await File.AppendAllTextAsync(fixture.Assets, " ", TestContext.CancellationToken);
                    if (state == "revoked") policy.Revoked = true;
                    if (state != "current")
                    {
                        if (state == "revoked")
                            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.PrepareTestRunAsync(status.WorkspaceId, null, null, CancellationToken.None));
                        else
                            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync(status.WorkspaceId, null, null, CancellationToken.None));
                        Assert.IsEmpty(runner.Commands, "Changed or revoked authority must never cause a replacement restore.");
                        return;
                    }
                    TestRunPlan plan = await service.PrepareTestRunAsync(status.WorkspaceId, null, null, CancellationToken.None);
                    Assert.IsTrue(plan.NoRestore);
                    Assert.AreEqual(native, plan.RequiresMtpNative);
                    Assert.IsEmpty(runner.Commands, "Current owned restore authority must be consumed without replacing its epoch.");
                    await service.RunTestCommandAsync(plan, CancellationToken.None);
                    Assert.HasCount(1, runner.Commands);
                    Assert.AreEqual("test", runner.Commands[0][0]);
                    CollectionAssert.Contains(runner.Commands[0], "--no-restore");
                    Assert.AreSequenceEqual(before, await File.ReadAllBytesAsync(fixture.Assets, TestContext.CancellationToken));
                    policy.Revoked = true;
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.RunTestCommandAsync(plan, CancellationToken.None));
                    Assert.HasCount(1, runner.Commands);

                }
                catch (Exception failure) { resourceCleanupFailure6 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure6, executor); }

            }
            catch (Exception failure) { resourceCleanupFailure5 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure5, manager); }

        }
        catch (Exception failure) { resourceCleanupFailure4 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure4, fixture); }
    }

    private sealed class RevocableTestInputPolicy(string path, IWorkspacePathPolicy owned) : IWorkspacePathPolicy
    {
        public bool Revoked { get; set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (Revoked && paths.Contains(path, FileSystemPath.Comparer))
                throw new UnauthorizedAccessException("The prepared test input grant was revoked.");
            return owned.ValidateAsync(workspaceId, paths, ct);
        }
    }

    [TestMethod]
    [DataRow(false, "copy")]
    [DataRow(true, "copy")]
    [DataRow(false, "target")]
    [DataRow(true, "target")]
    [DataRow(false, "properties")]
    [DataRow(true, "properties")]
    [DataRow(false, "workspace")]
    [DataRow(true, "workspace")]
    public async Task PreparedReceipt_BindsExactIssuedPlan(bool native, string mutation)
    {
        WorkspaceStatusDto status = native ? await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true) : Status();
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure7 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure8 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunPlan prepared = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                TestRunPlan changed = mutation switch
                {
                    "copy" => prepared with { },
                    "target" => prepared with { TargetPath = Path.Combine(_fixtureRoot, "Other.csproj") },
                    "properties" => prepared with { EvaluationProperties = prepared.EvaluationProperties.SetItem("TargetFramework", "net9.0") },
                    "workspace" => prepared with { WorkspaceId = "other" },
                    _ => throw new AssertFailedException("Unknown controlled mutation.")
                };
                var before = runner.Commands.Count;
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.RunTestCommandAsync(changed, CancellationToken.None));
                Assert.HasCount(before, runner.Commands, "Copied authority must refuse before command execution.");
                await service.RunTestCommandAsync(prepared, CancellationToken.None);
                Assert.HasCount(before + 1, runner.Commands);

            }
            catch (Exception failure) { resourceCleanupFailure8 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure8, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure7 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure7, workspace); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CertifiedMissingAssets_AreRestoredBeforeFreezingTestExecution(bool mutateSourceDuringCapture)
    {
        var project = Path.Combine(_fixtureRoot, "Tests.csproj");
        var assets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "global.json"), "{}", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "Directory.Build.props"), "<Project />", TestContext.CancellationToken);
        var source = Path.Combine(_fixtureRoot, "Test.cs");
        await File.WriteAllTextAsync(source, "class Test { }", TestContext.CancellationToken);
        var policy = new PreparationSourceMutationPolicy(project, source, assets, FixturePolicy);
        var manager = CreateOwnedManager(new ForkInvocationPathPolicy(policy));
        Exception? resourceCleanupFailure9 = null;
        try
        {
            WorkspaceStatusDto status = await manager.LoadAsync(project, OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()), CancellationToken.None);
            var certificate = NativeCompilerContextCertificate.Require(manager.GetCurrentSolution(status.WorkspaceId));
            Assert.IsTrue(certificate.InstalledPackages.AssetInputs.Any(input => !input.Exists && FileSystemPath.Comparer.Equals(input.FilePath, assets)));
            policy.Armed = mutateSourceDuringCapture;
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () => File.WriteAllText(assets, "{\"libraries\":{}}")
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure10 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                if (mutateSourceDuringCapture)
                {
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync(status.WorkspaceId, null, null, CancellationToken.None));
                    Assert.AreEqual(1, policy.Mutations, "Source drift must occur after certificate validation, during fresh authored-input capture.");
                    Assert.IsEmpty(runner.Commands, "An old certificate cannot authorize a producer after its source epoch changed.");
                    Assert.IsFalse(File.Exists(assets));
                    return;
                }
                TestRunPlan plan = await service.PrepareTestRunAsync(status.WorkspaceId, null, null, CancellationToken.None);
                Assert.HasCount(1, runner.Commands);
                await service.RunTestCommandAsync(plan, CancellationToken.None);
                Assert.HasCount(2, runner.Commands);
                CollectionAssert.Contains(runner.Commands[1], "--no-restore");

            }
            catch (Exception failure) { resourceCleanupFailure10 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure10, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure9 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure9, manager); }
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("absent-root")]
    [DataRow("recorded-root")]
    [DataRow("distinct-roots")]
    [DataRow("exhausted-budget")]
    public async Task CertifiedMissingAssets_PreserveRestorePrerequisiteContracts(string scenario)
    {
        string[] names = scenario == "recorded-root" ? ["Tests", "Prerequisite"] : ["Tests", "Dependency", "Prerequisite"];
        var paths = names.ToDictionary(name => name, name => Path.Combine(_fixtureRoot, name, name + ".csproj"));
        var roots = names.ToDictionary(name => name, name => Path.Combine(_fixtureRoot, "caches", name));
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths[name])!);
            var definition = new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Project",
                new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new System.Xml.Linq.XElement("PropertyGroup",
                    new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                    new System.Xml.Linq.XElement("IsTestProject", name == "Tests" ? "true" : "false"),
                    new System.Xml.Linq.XElement("NuGetAudit", "false"),
                    new System.Xml.Linq.XElement("RestorePackagesPath", roots[name]))));
            if (name != "Prerequisite")
            {
                var dependency = name == "Tests" && names.Contains("Dependency", StringComparer.Ordinal) ? "Dependency" : "Prerequisite";
                definition.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
                    new System.Xml.Linq.XElement("ProjectReference",
                        new System.Xml.Linq.XAttribute("Include", paths[dependency]),
                        new System.Xml.Linq.XAttribute("ReferenceOutputAssembly", "false"))));
            }
            await File.WriteAllTextAsync(paths[name], definition.ToString(), TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(paths[name])!, name + ".cs"), "class " + name + "Source { }", TestContext.CancellationToken);
        }
        // Noncompiler prerequisite edges remain native graph nodes; list them as
        // solution entries because ReferenceOutputAssembly=false does not ask
        // MSBuildWorkspace.OpenProjectAsync to load their semantic projects.
        var solutionPath = Path.Combine(_fixtureRoot, "Prerequisites.slnx");
        var solution = new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Solution",
            names.Select(name => new System.Xml.Linq.XElement("Project",
                new System.Xml.Linq.XAttribute("Path", Path.GetRelativePath(_fixtureRoot, paths[name]).Replace('\\', '/'))))));
        await File.WriteAllTextAsync(solutionPath, solution.ToString(), TestContext.CancellationToken);
        var properties = OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>());
        var restored = await new DotnetCommandRunner().RunAsync(_fixtureRoot, solutionPath,
            MsBuildCommandProperties.Append(["restore", solutionPath, "--nologo"], properties), TestContext.CancellationToken);
        Assert.IsTrue(restored.Succeeded, restored.StdErr + restored.StdOut);
        var assets = names.ToDictionary(name => paths[name],
            name => Path.Combine(Path.GetDirectoryName(paths[name])!, "obj", "project.assets.json"), FileSystemPath.Comparer);
        var bytes = new Dictionary<string, byte[]>(FileSystemPath.Comparer);
        foreach (var pair in assets) bytes.Add(pair.Key, await File.ReadAllBytesAsync(pair.Value, TestContext.CancellationToken));
        foreach (var name in names)
        {
            Assert.AreEqual(roots[name], RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(assets[paths[name]], CancellationToken.None),
                "The actual SDK restore must establish each independent recorded package root.");
            if (name == "Prerequisite" || scenario == "absent-root") File.Delete(assets[paths[name]]);
        }
        var policy = new ForkInvocationPathPolicy(FixturePolicy);
        var manager = CreateOwnedManager(policy);
        Exception? primary = null;
        try
        {
            WorkspaceStatusDto status;
            try { status = await manager.LoadAsync(solutionPath, properties, TestContext.CancellationToken); }
            catch (PhysicalGraphMismatchException failure)
            {
                foreach (var key in new[] { "ExecutionEntryCounts", "ExecutionNodeCounts", "ExecutionIdentityMismatchComponents", "ExecutionContextMismatchComponents" })
                    TestContext.WriteLine(key + "=" + failure.Data[key]);
                throw;
            }
            var certificate = NativeCompilerContextCertificate.Require(manager.GetCurrentSolution(status.WorkspaceId));
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, certificate.Availability);
            Assert.IsTrue(certificate.InstalledPackages.AssetInputs.Any(input => !input.Exists
                && FileSystemPath.Comparer.Equals(input.FilePath, assets[paths["Prerequisite"]])),
                "A genuinely Certified missing-assets dependency must require producer execution.");
            var runner = new PrerequisiteRestoreRunner(scenario != "failure", assets, bytes);
            var realExecutor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            var executor = new RestoreTimeoutRecorder(realExecutor, static () => { });
            Exception? resourceCleanupFailure11 = null;
            try
            {
                var service = new TestRunnerService(policy, manager, executor, NullLogger<TestRunnerService>.Instance,
                    new UnusedDiscovery(), new ValidationServiceOptions
                    {
                        BuildTimeout = scenario == "exhausted-budget" ? TimeSpan.Zero : TimeSpan.FromMinutes(2)
                    });
                if (scenario == "exhausted-budget")
                {
                    await Assert.ThrowsAsync<TimeoutException>(() => service.RunTestsAsync(status.WorkspaceId, "Tests", null, TestContext.CancellationToken));
                    Assert.IsEmpty(executor.Timeouts);
                    Assert.IsEmpty(runner.Commands, "An exhausted shared budget must produce zero process effects.");
                    return;
                }
                if (scenario == "failure")
                {
                    var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                        service.RunTestsAsync(status.WorkspaceId, "Tests", null, TestContext.CancellationToken));
                    StringAssert.Contains(failure.Message, "restore");
                    Assert.HasCount(1, runner.Commands);
                    Assert.AreEqual("restore", runner.Commands.Single()[0]);
                    return;
                }
                var plan = await service.PrepareTestRunAsync(status.WorkspaceId, "Tests", null, TestContext.CancellationToken);
                Assert.HasCount(names.Length, runner.Commands, "Every selected dependency is restored once without crossing its package destination.");
                foreach (var name in names)
                {
                    var argv = runner.Commands.Single(arguments => FileSystemPath.Comparer.Equals(arguments[1], paths[name]));
                    CollectionAssert.Contains(argv, "--no-dependencies");
                    var index = Array.IndexOf(argv, "--packages");
                    if (scenario == "absent-root" || name == "Prerequisite")
                        Assert.AreEqual(-1, index, "A missing recorded root preserves native configured/default argv.");
                    else
                    {
                        Assert.IsTrue(index >= 0);
                        Assert.AreEqual(roots[name], argv[index + 1]);
                    }
                }
                Assert.HasCount(names.Length, executor.Timeouts);
                Assert.IsTrue(executor.Timeouts.Zip(executor.Timeouts.Skip(1)).All(pair => pair.Second < pair.First),
                    "Every native restore consumes the same original total budget.");
                Assert.IsTrue(runner.Commands.All(arguments => arguments[0] == "restore"), "Preparation must not start a test process.");
                Assert.IsNotNull(plan);

            }
            catch (Exception failure) { resourceCleanupFailure11 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure11, executor); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Certified prerequisite manager cleanup failed.", primary, manager); }
    }

    private sealed class PrerequisiteRestoreRunner(bool succeeds, IReadOnlyDictionary<string, string> assets,
        IReadOnlyDictionary<string, byte[]> bytes) : CannedCommandRunner
    {
        internal List<string[]> Commands { get; } = [];
        public override async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add([.. arguments]);
            Assert.AreEqual("restore", arguments[0], "Prerequisite preparation must not execute tests.");
            if (succeeds) await File.WriteAllBytesAsync(assets[targetPath], bytes[targetPath], ct);
            return new("dotnet", arguments, workingDirectory, targetPath, succeeds ? 0 : 1, succeeds, 1, "", "");
        }
    }

    [TestMethod]
    [DataRow("nested-response", "restore")]
    [DataRow("included-ruleset", "restore")]
    [DataRow("analyzer-directory-member", "restore")]
    [DataRow("nested-response", "prepared")]
    [DataRow("included-ruleset", "prepared")]
    [DataRow("analyzer-directory-member", "prepared")]
    public async Task CertifiedMissingAssets_CurrentCallbacksCannotChangeHiddenCompilerInputs(string mutation, string phase)
    {
        if (mutation == "analyzer-directory-member" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Held analyzer catalog membership is the Windows shadow contract.");
        var project = Path.Combine(_fixtureRoot, "Tests.csproj");
        var cache = Path.Combine(_fixtureRoot, "configured-cache");
        var nested = Path.Combine(_fixtureRoot, "nested.rsp");
        var included = Path.Combine(_fixtureRoot, "included.ruleset");
        var analyzers = Path.Combine(_fixtureRoot, "analyzers");
        Directory.CreateDirectory(analyzers);
        var analyzer = Path.Combine(analyzers, "OwnedCompilerLibrary.dll");
        var analyzerBytes = await File.ReadAllBytesAsync(typeof(Compilation).Assembly.Location, TestContext.CancellationToken);
        await File.WriteAllBytesAsync(analyzer, analyzerBytes, TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "outer.rsp"), "@nested.rsp\n", TestContext.CancellationToken);
        await File.WriteAllTextAsync(nested, "/ruleset:root.ruleset\n", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "root.ruleset"),
            "<RuleSet Name=\"Root\" ToolsVersion=\"15.0\"><Include Path=\"included.ruleset\" Action=\"Default\" /></RuleSet>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(included, "<RuleSet Name=\"Included\" ToolsVersion=\"15.0\" />", TestContext.CancellationToken);
        var definition = System.Xml.Linq.XDocument.Load(project);
        definition.Root!.Element("PropertyGroup")!.Add(
            new System.Xml.Linq.XElement("CompilerResponseFile", "outer.rsp"),
            new System.Xml.Linq.XElement("RestorePackagesPath", cache));
        definition.Root.Add(new System.Xml.Linq.XElement("ItemGroup",
            new System.Xml.Linq.XElement("Analyzer", new System.Xml.Linq.XAttribute("Include", analyzer)),
            new System.Xml.Linq.XElement("None", new System.Xml.Linq.XAttribute("Remove", "*.rsp;*.ruleset;analyzers/**"))));
        await File.WriteAllTextAsync(project, definition.ToString(), TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "Test.cs"), "class Test { }", TestContext.CancellationToken);
        var assets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
        byte[]? selectingBytes = null;
        if (phase == "prepared")
        {
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var nativeRestore = await new DotnetCommandRunner().RunAsync(_fixtureRoot, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), TestContext.CancellationToken);
            Assert.IsTrue(nativeRestore.Succeeded, nativeRestore.StdErr + nativeRestore.StdOut);
            selectingBytes = await File.ReadAllBytesAsync(assets, TestContext.CancellationToken);
            File.Delete(assets);
        }
        var policy = new DestinationCompilerMutationPolicy(FixturePolicy,
            paths => phase == "restore"
                ? paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], cache)
                : paths.Contains(project, FileSystemPath.Comparer) && paths.Contains(assets, FileSystemPath.Comparer)
                    && paths.Contains(Path.Combine(_fixtureRoot, "Test.cs"), FileSystemPath.Comparer),
            async ct =>
        {
            if (mutation == "nested-response") await File.AppendAllTextAsync(nested, "/define:CHANGED_AFTER_DESTINATION\n", ct);
            else if (mutation == "included-ruleset") await File.WriteAllTextAsync(included,
                "<RuleSet Name=\"ChangedIncluded\" ToolsVersion=\"15.0\" />", ct);
            else await File.WriteAllBytesAsync(Path.Combine(analyzers, "NewMatchingLibrary.dll"), analyzerBytes, ct);
        });
        var invocationPolicy = new ForkInvocationPathPolicy(policy);
        var manager = CreateOwnedManager(invocationPolicy);
        Exception? primary = null;
        try
        {
            var status = await manager.LoadAsync(project, OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()), TestContext.CancellationToken);
            var solution = manager.GetCurrentSolution(status.WorkspaceId);
            var certificate = NativeCompilerContextCertificate.Require(solution);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, certificate.Availability);
            Assert.IsTrue(certificate.InstalledPackages.AssetInputs.Any(input => !input.Exists));
            CollectionAssert.Contains(certificate.FinalRestoreGrantPaths(solution).ToArray(), nested);
            CollectionAssert.Contains(certificate.FinalRestoreGrantPaths(solution).ToArray(), included);
            if (OperatingSystem.IsWindows()) CollectionAssert.Contains(certificate.FinalRestoreGrantPaths(solution).ToArray(), analyzers);
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = selectingBytes is { } captured ? () => File.WriteAllBytes(assets, captured) : null
            };
            var executor = new RestoreTimeoutRecorder(
                new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance), () => policy.Armed = phase == "restore");
            Exception? resourceCleanupFailure12 = null;
            try
            {
                var service = new TestRunnerService(invocationPolicy, manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                if (phase == "prepared")
                {
                    var prepared = await service.PrepareTestRunAsync(status.WorkspaceId, null, null, TestContext.CancellationToken);
                    Assert.HasCount(1, runner.Commands);
                    Assert.AreEqual("restore", runner.Commands.Single()[0]);
                    Assert.AreEqual(0, policy.Mutations);
                    Assert.IsNotNull(selectingBytes);
                    CollectionAssert.AreEqual(selectingBytes, await File.ReadAllBytesAsync(assets, TestContext.CancellationToken));
                    policy.Armed = true;
                    Exception? stale = null;
                    try { await service.AssertPreparedTestInputsAsync(prepared, TestContext.CancellationToken); }
                    catch (Exception failure) { stale = failure; }
                    Assert.AreEqual(1, policy.Mutations, "The actual issued receipt must encounter the changed hidden input at current prepared admission.");
                    if (mutation == "nested-response")
                        StringAssert.Contains(await File.ReadAllTextAsync(nested, TestContext.CancellationToken), "CHANGED_AFTER_DESTINATION");
                    else if (mutation == "included-ruleset")
                        StringAssert.Contains(await File.ReadAllTextAsync(included, TestContext.CancellationToken), "ChangedIncluded");
                    else
                        CollectionAssert.AreEqual(analyzerBytes, await File.ReadAllBytesAsync(Path.Combine(analyzers, "NewMatchingLibrary.dll"), TestContext.CancellationToken));
                    Assert.HasCount(1, runner.Commands, "The prepared-input helper must not execute another producer.");
                    Assert.AreEqual(typeof(PublicInvalidOperationException), stale?.GetType());
                    return;
                }
                Exception? refused = null;
                try { await service.PrepareTestRunAsync(status.WorkspaceId, null, null, TestContext.CancellationToken); }
                catch (Exception failure) { refused = failure; }
                Assert.AreEqual(1, policy.Mutations, "The hidden input must change after capture, during current destination authorization at the actual restore gate.");
                Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType());
                Assert.IsEmpty(runner.Commands, "A hidden compiler dependency change must refuse before any restore/test process.");

            }
            catch (Exception failure) { resourceCleanupFailure12 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure12, executor); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Certified prerequisite manager cleanup failed.", primary, manager); }
    }

    private sealed class DestinationCompilerMutationPolicy(IWorkspacePathPolicy inner, Func<IReadOnlyList<string>, bool> matches,
        Func<CancellationToken, Task> mutate) : IWorkspacePathPolicy
    {
        internal bool Armed { get; set; }
        internal int Mutations { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await inner.ValidateAsync(workspaceId, paths, ct);
            if (!Armed || Mutations != 0 || !matches(paths)) return;
            Mutations++;
            await mutate(ct);
        }
    }

    private sealed class RestoreTimeoutRecorder(IGatedCommandExecutor inner, Action observeRestoreStart) : IGatedCommandExecutor
    {
        internal List<TimeSpan> Timeouts { get; } = [];
        public Task<CommandExecutionDto> ExecuteAuthorizedAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
        {
            Timeouts.Add(timeout);
            observeRestoreStart();
            return inner.ExecuteAuthorizedAsync(workspaceId, targetPath, arguments, timeout, authorizeStart, ct);
        }
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken ct) => inner.ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct);
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? patterns, CancellationToken ct) =>
            inner.ExecuteAsync(workspaceId, targetPath, arguments, timeout, patterns, ct);
        public Task<TestRunResultDto> ExecuteTestAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults, TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? patterns, Func<CancellationToken, Task> authorizeStart, CancellationToken ct) =>
            inner.ExecuteTestAsync(workspaceId, targetPath, arguments, collectResults, timeout, patterns, authorizeStart, ct);
        public Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct) =>
            inner.ExecuteBuildAsync(plan, timeout, ct);
        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) => inner.ResolveProject(workspaceId, projectName);
        public void Dispose() => inner.Dispose();
    }

    private sealed class PreparationSourceMutationPolicy(string project, string source, string assets, IWorkspacePathPolicy owned) : IWorkspacePathPolicy
    {
        internal bool Armed { get; set; }
        internal int Mutations { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await owned.ValidateAsync(workspaceId, paths, ct);
            if (Armed && Mutations == 0 && paths.Contains(project, FileSystemPath.Comparer)
                && paths.Contains(source, FileSystemPath.Comparer) && !paths.Contains(assets, FileSystemPath.Comparer))
            {
                Mutations++;
                await File.WriteAllTextAsync(source, "class ChangedAfterCertification { }", ct);
            }
        }
    }

    [TestMethod]
    public async Task CertifiedUnrelatedMissingAssets_DoNotRestoreCurrentSelectedTestProject()
    {
        var project = Path.Combine(_fixtureRoot, "Tests.csproj");
        var applicationDirectory = Path.Combine(_fixtureRoot, "Application");
        Directory.CreateDirectory(applicationDirectory);
        var application = Path.Combine(applicationDirectory, "Application.csproj");
        var solutionPath = Path.Combine(_fixtureRoot, "Selected.slnx");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Remove=\"Application/**\" /></ItemGroup></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(application, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "Test.cs"), "class SelectedTestSource { }", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(applicationDirectory, "Application.cs"), "class UnrelatedApplicationSource { }", TestContext.CancellationToken);
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"Tests.csproj\" /><Project Path=\"Application/Application.csproj\" /></Solution>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "global.json"), "{}", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "Directory.Build.props"), "<Project />", TestContext.CancellationToken);
        Dictionary<string, string> properties = OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>());
        CommandExecutionDto restore = await new DotnetCommandRunner().RunAsync(_fixtureRoot, project,
            MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), CancellationToken.None);
        Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
        var assets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
        var missingAssets = Path.Combine(applicationDirectory, "obj", "project.assets.json");
        var before = await File.ReadAllBytesAsync(assets, TestContext.CancellationToken);
        var policy = FixturePolicy;
        var manager = CreateOwnedManager(new ForkInvocationPathPolicy(policy));
        Exception? resourceCleanupFailure13 = null;
        try
        {
            WorkspaceStatusDto status = await manager.LoadAsync(solutionPath, properties, CancellationToken.None);
            var certificate = NativeCompilerContextCertificate.Require(manager.GetCurrentSolution(status.WorkspaceId));
            Assert.IsTrue(certificate.InstalledPackages.AssetInputs.Any(input => !input.Exists && FileSystemPath.Comparer.Equals(input.FilePath, missingAssets)));
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure14 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunPlan plan = await service.PrepareTestRunAsync(status.WorkspaceId, "Tests", null, CancellationToken.None);
                Assert.IsEmpty(runner.Commands, "Unrelated missing assets must not start a replacement restore.");
                await service.RunTestCommandAsync(plan, CancellationToken.None);
                Assert.HasCount(1, runner.Commands);
                CollectionAssert.Contains(runner.Commands[0], "--no-restore");
                Assert.AreSequenceEqual(before, await File.ReadAllBytesAsync(assets, TestContext.CancellationToken));
                Assert.IsFalse(File.Exists(missingAssets));

            }
            catch (Exception failure) { resourceCleanupFailure14 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure14, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure13 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure13, manager); }
    }

    [TestMethod]
    [DataRow(false, "unchanged")]
    [DataRow(true, "unchanged")]
    [DataRow(false, "changed")]
    [DataRow(true, "changed")]
    [DataRow(false, "added")]
    [DataRow(true, "added")]
    [DataRow(false, "condition")]
    [DataRow(true, "condition")]
    [DataRow(false, "lateadded")]
    [DataRow(true, "lateadded")]
    public async Task RestorePreparation_AdvancesOnlyAssetsAndPreservesAuthoredSources(bool assetsExist, string sourceMutation)
    {
        WorkspaceStatusDto status = Status();
        var project = status.Projects.Single().FilePath;
        var assets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
        var source = Path.Combine(_fixtureRoot, "Test.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "conditions"));
        await NativeSdkRestoreFixture.WriteProjectAsync(project, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup><PropertyGroup Condition=\"Exists('switch.flag')\"><DefineConstants>RESTORE_CHANGED_CONTEXT</DefineConstants></PropertyGroup><PropertyGroup Condition=\"Exists('conditions/')\"><DefineConstants>$(DefineConstants);EXISTING_DIRECTORY</DefineConstants></PropertyGroup><ItemGroup><Compile Include=\"*.cs\" /></ItemGroup></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(source, "class Before { }", TestContext.CancellationToken);
        if (assetsExist) await File.WriteAllTextAsync(assets, "{}", TestContext.CancellationToken);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure15 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () =>
                {
                    File.WriteAllText(assets, "{\"libraries\":{}}");
                    if (sourceMutation == "changed") File.WriteAllText(source, "class Changed { }");
                    if (sourceMutation == "added") File.WriteAllText(Path.Combine(_fixtureRoot, "Added.cs"), "class Added { }");
                    if (sourceMutation == "condition") File.WriteAllText(Path.Combine(_fixtureRoot, "switch.flag"), "enabled");
                }
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure16 = null;
            try
            {
                var latePolicy = new FinalPreparationMutationPolicy(source, assets, Path.Combine(_fixtureRoot, "LateAdded.cs"), FixturePolicy);
                IWorkspacePathPolicy policy = sourceMutation == "lateadded" ? latePolicy : FixturePolicy;
                var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                if (sourceMutation != "unchanged")
                {
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                    Assert.HasCount(1, runner.Commands);
                    if (sourceMutation == "lateadded") Assert.AreEqual(1, latePolicy.Mutations);
                    return;
                }
                TestRunPlan prepared = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                Assert.HasCount(1, runner.Commands);
                await service.RunTestCommandAsync(prepared, CancellationToken.None);
                Assert.HasCount(2, runner.Commands);
                CollectionAssert.Contains(runner.Commands[1], "--no-restore");
                await File.AppendAllTextAsync(assets, " ", TestContext.CancellationToken);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.RunTestCommandAsync(prepared, CancellationToken.None));
                Assert.HasCount(2, runner.Commands);

            }
            catch (Exception failure) { resourceCleanupFailure16 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure16, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure15 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure15, workspace); }
    }

    private sealed class FinalPreparationMutationPolicy(string source, string assets, string added, IWorkspacePathPolicy owned) : IWorkspacePathPolicy
    {
        internal int Mutations { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await owned.ValidateAsync(workspaceId, paths, ct);
            // Only the final execution-input union contains both the preserved
            // source and the newly restored assets; pre-restore proof excludes assets.
            if (Mutations == 0 && paths.Contains(source, FileSystemPath.Comparer) && paths.Contains(assets, FileSystemPath.Comparer))
            {
                Mutations++;
                await File.WriteAllTextAsync(added, "class LateAdded {}", ct);
            }
        }
    }

    [TestMethod]
    public async Task SolutionRestore_PreservesUnselectedProjectMembership()
    {
        var solutionPath = Path.Combine(_fixtureRoot, "All.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"A/A.csproj\"/><Project Path=\"B/B.csproj\"/><Project Path=\"App/App.csproj\"/></Solution>", TestContext.CancellationToken);
        var workspace = CreateOwnedEvaluationWorkspace(solutionPath);
        Exception? resourceCleanupFailure17 = null;
        try
        {
            var statuses = new List<ProjectStatusDto>();
            foreach (var name in new[] { "A", "B", "App" })
            {
                var directory = Path.Combine(_fixtureRoot, name);
                Directory.CreateDirectory(Path.Combine(directory, "obj"));
                var project = Path.Combine(directory, name + ".csproj");
                var test = name != "App";
                await NativeSdkRestoreFixture.WriteProjectAsync(project, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>" + test.ToString().ToLowerInvariant()
                    + "</IsTestProject><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup><ItemGroup><Compile Include=\"*.cs\"/></ItemGroup></Project>", TestContext.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(directory, "Entry.cs"), "class " + name + " {}", TestContext.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(directory, "obj", "project.assets.json"), "{}", TestContext.CancellationToken);
                statuses.Add(new(name, project, 0, [], ["net10.0"], test, name, "Library"));
            }
            Solution solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual(3, solution.ProjectIds.Count);
            foreach (var projectStatus in statuses)
                Assert.IsTrue(solution.Projects.Any(project => FileSystemPath.Comparer.Equals(project.FilePath, projectStatus.FilePath)));
            var status = new WorkspaceStatusDto("ws", solutionPath, 1, "snapshot", DateTimeOffset.UtcNow, 3, 0, statuses, true, false, []);
            FailClosedWorkspaceManagerStub manager = NativeManager(status, solution);
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () => File.WriteAllText(Path.Combine(_fixtureRoot, "App", "New.cs"), "class New {}")
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure18 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                Assert.HasCount(1, runner.Commands, "The first selected producer changed an unselected project's membership; no second producer or test may start.");

            }
            catch (Exception failure) { resourceCleanupFailure18 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure18, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure17 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure17, workspace); }
    }

    [TestMethod]
    public async Task SharedDependencyAssets_AreFrozenAfterEverySelectedProjectRestore()
    {
        var solutionPath = Path.Combine(_fixtureRoot, "Tests.slnx");
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "global.json"), "{}", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "Directory.Build.props"), "<Project />", TestContext.CancellationToken);
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"A/A.csproj\" /><Project Path=\"B/B.csproj\" /><Project Path=\"Shared/Shared.csproj\" /></Solution>", TestContext.CancellationToken);
        var statuses = new List<ProjectStatusDto>();
        var workspace = MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>()));
        Exception? resourceCleanupFailure19 = null;
        try
        {
            Solution solution = workspace.CurrentSolution;
            foreach (var name in new[] { "A", "B", "Shared" })
            {
                var directory = Path.Combine(_fixtureRoot, name);
                Directory.CreateDirectory(Path.Combine(directory, "obj"));
                var project = Path.Combine(directory, name + ".csproj");
                await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><IsTestProject>"
                    + (name != "Shared" ? "true" : "false") + "</IsTestProject></PropertyGroup>"
                    + (name != "Shared" ? "<ItemGroup><ProjectReference Include=\"../Shared/Shared.csproj\" /></ItemGroup>" : "") + "</Project>", TestContext.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(directory, "obj", "project.assets.json"), "{}", TestContext.CancellationToken);
                solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), name, name,
                    LanguageNames.CSharp, filePath: project));
                statuses.Add(new(name, project, 0, [], ["net10.0"], name != "Shared", name, "Library"));
            }
            var status = new WorkspaceStatusDto("ws", solutionPath, 1, "snapshot", DateTimeOffset.UtcNow, 3, 0,
                statuses, true, false, []);
            FailClosedWorkspaceManagerStub manager = NativeManager(status, solution);
            var sharedAssets = Path.Combine(_fixtureRoot, "Shared", "obj", "project.assets.json");
            var generation = 0;
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () => File.WriteAllText(sharedAssets, "{\"libraries\":{},\"restoreGeneration\":" + (++generation) + "}")
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure20 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                Assert.AreEqual(3, generation, "The selected dependency union must restore each shared native node exactly once before final capture.");
                Assert.HasCount(3, runner.Commands);
                Assert.IsTrue(runner.Commands.All(command => command[0] == "restore"));
                Assert.AreEqual(Path.Combine(_fixtureRoot, "Shared", "Shared.csproj"), runner.Commands[0][1],
                    "The dependency must restore before either selected project.");
                CollectionAssert.AreEquivalent(new[] { Path.Combine(_fixtureRoot, "A", "A.csproj"), Path.Combine(_fixtureRoot, "B", "B.csproj") },
                    runner.Commands.Skip(1).Select(command => command[1]).ToArray());
                DotnetConfigurationIdentity identity = plan.Context.ExecutionInputs.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, sharedAssets));
                Assert.AreSequenceEqual(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sharedAssets, TestContext.CancellationToken)), identity.ContentHash.ToArray());
                await service.RunTestCommandAsync(plan, CancellationToken.None);
                Assert.HasCount(4, runner.Commands);
                Assert.AreEqual("test", runner.Commands[3][0]);
                CollectionAssert.Contains(runner.Commands[3], "--no-restore");

            }
            catch (Exception failure) { resourceCleanupFailure20 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure20, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure19 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure19, workspace); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestoreOutputUsedAsAuthoredAdditionalFile_RemainsProtected(bool exists)
    {
        WorkspaceStatusDto status = Status();
        var project = status.Projects.Single().FilePath;
        var assets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        await NativeSdkRestoreFixture.WriteProjectAsync(project, "<Project><PropertyGroup><IsTestProject>true</IsTestProject><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup><ItemGroup><AdditionalFiles Include=\"obj/project.assets.json\" /></ItemGroup></Project>", TestContext.CancellationToken);
        if (exists) await File.WriteAllTextAsync(assets, "{}", TestContext.CancellationToken);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure21 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () => File.WriteAllText(assets, "{\"libraries\":{}}")
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure22 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                Assert.HasCount(0, runner.Commands, "An authored compiler input cannot become a mutable restore output; refusal precedes every producer.");
                Assert.AreEqual(exists, File.Exists(assets), "Refusal must preserve the original file or its exact absence.");
                if (exists) Assert.AreEqual("{}", await File.ReadAllTextAsync(assets, TestContext.CancellationToken));

            }
            catch (Exception failure) { resourceCleanupFailure22 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure22, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure21 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure21, workspace); }
    }

    [TestMethod]
    public async Task PreparedRun_CapturesEvaluationPropertiesBeforeWorkspaceChanges()
    {
        MsBuildInitializer.EnsureInitialized();
        var original = MSBuildWorkspace.Create(new Dictionary<string, string> { ["UnknownSetting"] = Sentinel });
        Exception? resourceCleanupFailure23 = null;
        try
        {
            var replacement = new AdhocWorkspace();
            Exception? resourceCleanupFailure24 = null;
            try
            {
                WorkspaceStatusDto status = Status();
                Solution current = SolutionFor(original, status);
                var manager = new FailClosedWorkspaceManagerStub(status)
                {
                    GetStatusAsyncHandler = _ => status,
                    GetCurrentSolutionHandler = _ => current,
                    GetCurrentVersionHandler = _ => 1
                };
                var runner = new RecordingRunner { RestoreSucceeds = true };
                var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
                Exception? resourceCleanupFailure25 = null;
                try
                {
                    var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                    TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                    Assert.IsTrue(plan.NoRestore);
                    Assert.HasCount(1, runner.Commands);
                    Assert.AreEqual("restore", runner.Commands[0][0]);
                    AssertRedacted(plan.ToString());
                    AssertRedacted(JsonSerializer.Serialize(plan));
                    current = replacement.CurrentSolution;
                    await service.RunTestCommandAsync(plan, CancellationToken.None);
                    CollectionAssert.Contains(runner.Arguments.ToArray(), "--no-restore");
                    CollectionAssert.Contains(runner.Arguments.ToArray(), "-p:UnknownSetting=" + Encoded);

                }
                catch (Exception failure) { resourceCleanupFailure25 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure25, executor); }

            }
            catch (Exception failure) { resourceCleanupFailure24 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure24, replacement); }

        }
        catch (Exception failure) { resourceCleanupFailure23 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure23, original); }
    }

    [TestMethod]
    [DataRow("MSTest.TestFramework", "4.5.1", "--report-trx")]
    [DataRow("xunit.v3", "4.0.1", "--report-xunit-trx")]
    public async Task CapturedNativeMode_SelectsFrameworkReporterAndFreezesDependencyInputs(string package, string version, string reporter)
    {
        WorkspaceStatusDto status = await NativeFixtureAsync(package, version, reporter == "--report-trx", capable: true);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure26 = null;
        try
        {
            Solution solution = SolutionFor(workspace, status);
            FailClosedWorkspaceManagerStub manager = NativeManager(status, solution);
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure27 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                Assert.IsTrue(plan.RequiresMtpNative);
                Assert.IsTrue(plan.NoRestore);
                Assert.IsNotEmpty(plan.Context.ExecutionInputs);
                CollectionAssert.Contains(plan.NativeTestArguments.ToArray(), reporter);
                Assert.HasCount(1, runner.Commands);
                Assert.AreEqual("restore", runner.Commands[0][0]);
                await service.RunTestCommandAsync(plan, CancellationToken.None);
                CollectionAssert.Contains(runner.Arguments.ToArray(), "--project");
                CollectionAssert.Contains(runner.Arguments.ToArray(), reporter);
                CollectionAssert.Contains(runner.Arguments.ToArray(), "--no-restore");
                CollectionAssert.DoesNotContain(runner.Arguments.ToArray(), "--logger");
                CollectionAssert.DoesNotContain(runner.Arguments.ToArray(), "--treenode-filter");

            }
            catch (Exception failure) { resourceCleanupFailure27 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure27, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure26 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure26, workspace); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestoreMutatesAuthoredCapabilityOrImport_RefusesInsteadOfMintingStaleNativePlan(bool imported)
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true);
        var projectPath = status.Projects[0].FilePath;
        var import = Path.Combine(_fixtureRoot, "NativeControl.props");
        if (imported)
        {
            await File.WriteAllTextAsync(import, "<Project><PropertyGroup><IsTestingPlatformApplication>true</IsTestingPlatformApplication></PropertyGroup></Project>", TestContext.CancellationToken);
            await File.WriteAllTextAsync(projectPath, File.ReadAllText(projectPath).Replace("</Project>", "<Import Project=\"NativeControl.props\" /></Project>", StringComparison.Ordinal), TestContext.CancellationToken);
        }
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure28 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var changed = imported ? import : projectPath;
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () => File.WriteAllText(changed, File.ReadAllText(changed).Replace(
                    "<IsTestingPlatformApplication>true</IsTestingPlatformApplication>", "<IsTestingPlatformApplication>false</IsTestingPlatformApplication>", StringComparison.Ordinal))
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure29 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                Assert.HasCount(1, runner.Commands);
                Assert.AreEqual("restore", runner.Commands[0][0]);

            }
            catch (Exception failure) { resourceCleanupFailure29 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure29, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure28 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure28, workspace); }
    }

    [TestMethod]
    public async Task NativeMode_VSTestOnlyProjectRefusesBeforeAnyCommand()
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: false);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure30 = null;
        try
        {
            var runner = new RecordingRunner { RestoreSucceeds = true };
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure31 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                PublicInvalidOperationException failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                Assert.Contains("VSTest-only", failure.Message);
                Assert.IsEmpty(runner.Commands);

            }
            catch (Exception failure) { resourceCleanupFailure31 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure31, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure30 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure30, workspace); }
    }

    [TestMethod]
    public async Task NativeMode_MissingReporterPropagatesRecordedRunnerFailureEnvelope()
    {
        const string cliError = "Unknown option '--report-trx'.";
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: false, capable: true);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure32 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var runner = new RecordingRunner { RestoreSucceeds = true, StdOutOverride = cliError };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure33 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunResultDto result = await service.RunTestsAsync("ws", null, null, CancellationToken.None);
                Assert.IsFalse(result.Execution.Succeeded);
                Assert.AreEqual(0, result.Total);
                Assert.IsNotNull(result.FailureEnvelope);
                Assert.AreEqual("Unknown", result.FailureEnvelope.ErrorKind);
                Assert.Contains(cliError, result.Execution.StdOut);
                Assert.HasCount(2, runner.Commands);
                Assert.AreEqual("restore", runner.Commands[0][0]);
                Assert.AreEqual("test", runner.Commands[1][0]);
                CollectionAssert.Contains(runner.Commands[1], "--report-trx");
                CollectionAssert.Contains(runner.Commands[1], "--no-restore");

            }
            catch (Exception failure) { resourceCleanupFailure33 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure33, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure32 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure32, workspace); }
    }

    [TestMethod]
    public async Task NativePreparedAssetsMutation_RefusesBeforeTestCommand()
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure34 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure35 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                await File.AppendAllTextAsync(Path.Combine(_fixtureRoot, "obj", "project.assets.json"), " ", TestContext.CancellationToken);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.RunTestCommandAsync(plan, CancellationToken.None));
                Assert.HasCount(1, runner.Commands);

            }
            catch (Exception failure) { resourceCleanupFailure35 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure35, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure34 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure34, workspace); }
    }

    [TestMethod]
    [DataRow("restore")]
    [DataRow("empty-inputs")]
    [DataRow("default-inputs")]
    public async Task NativePreparedPlanCannotDiscardFrozenExecutionAuthority(string mutation)
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure36 = null;
        try
        {
            FailClosedWorkspaceManagerStub manager = NativeManager(status, SolutionFor(workspace, status));
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure37 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunPlan prepared = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                Assert.IsTrue(prepared.RequiresMtpNative);
                Assert.IsTrue(prepared.NoRestore);
                Assert.IsFalse(prepared.Context.ExecutionInputs.IsDefaultOrEmpty);
                Assert.HasCount(1, runner.Commands);
                TestRunPlan changed = mutation switch
                {
                    "restore" => prepared with { NoRestore = false },
                    "empty-inputs" => prepared with { Context = prepared.Context with { ExecutionInputs = [] } },
                    "default-inputs" => prepared with { Context = prepared.Context with { ExecutionInputs = default } },
                    _ => throw new AssertFailedException("Unknown controlled mutation.")
                };
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.RunTestCommandAsync(changed, CancellationToken.None));
                Assert.HasCount(1, runner.Commands, "Invalid native authority must refuse before another command.");

            }
            catch (Exception failure) { resourceCleanupFailure37 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure37, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure36 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure36, workspace); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativePreparation_RechecksLaterAssetsGrantAfterEarlierAssetsProof(bool revoke)
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true);
        var projectPath = status.Projects[0].FilePath;
        var dependency = Path.Combine(_fixtureRoot, "Dependency.csproj");
        await NativeSdkRestoreFixture.WriteProjectAsync(dependency, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>dependency-assets.json</ProjectAssetsFile></PropertyGroup></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "dependency-assets.json"), "{}", TestContext.CancellationToken);
        var definition = System.Xml.Linq.XDocument.Load(projectPath);
        definition.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
            new System.Xml.Linq.XElement("ProjectReference", new System.Xml.Linq.XAttribute("Include", "Dependency.csproj"))));
        await File.WriteAllTextAsync(projectPath, definition.ToString(), TestContext.CancellationToken);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure38 = null;
        try
        {
            Solution solution = SolutionFor(workspace, status);
            RestoreInvocationPlanner.SelectedGraph selected = RestoreInvocationPlanner.Select(solution.Projects.Single(), EvaluatedBuildGraph.Source(solution, CancellationToken.None), CancellationToken.None);
            var assets = selected.Nodes.Select(node => node.Inputs.AssetsPath).OfType<string>()
                .Distinct(FileSystemPath.Comparer).ToArray();
            Assert.HasCount(2, assets, "The selected native graph must contain both assets selectors.");
            var first = assets[0];
            var later = assets[1];
            var restoreTargets = selected.Nodes.Where(node => !node.IsOuterBuild).Select(node => node.ProjectPath)
                .Distinct(FileSystemPath.Comparer).ToArray();
            Assert.HasCount(2, restoreTargets, "Both independent selected projects require their own restore command.");
            FailClosedWorkspaceManagerStub manager = NativeManager(status, solution);
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure39 = null;
            try
            {
                var batchApproved = false;
                var earlierProofReached = false;
                var denied = 0;
                FileStream? blocker = null;
                Exception? primary = null;
                try
                {
                    var owned = FixturePolicy;
                    var policy = new CallbackInputPolicy(async (workspaceId, paths, ct) =>
                    {
                        if (earlierProofReached && revoke && paths.Contains(later, FileSystemPath.Comparer))
                        {
                            denied++;
                            throw new UnauthorizedAccessException("The later assets grant was revoked during the earlier assets proof.");
                        }
                        await owned.ValidateAsync(workspaceId, paths, ct);
                        // Queued-start validation also approves this exact assets batch.
                        // It is not the execution-input boundary until BOTH native restore
                        // targets have completed; never retain an earlier batch approval.
                        var completedTargets = runner.Commands.Where(command => command[0] == "restore")
                            .Select(command => command[1]).ToHashSet(FileSystemPath.Comparer);
                        if (!completedTargets.SetEquals(restoreTargets)) return;
                        if (paths.Count == assets.Length && paths.ToHashSet(FileSystemPath.Comparer).SetEquals(assets))
                            batchApproved = true;
                        if (batchApproved && !earlierProofReached && paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], first))
                        {
                            earlierProofReached = true;
                            if (revoke) blocker = new FileStream(later, FileMode.Open, FileAccess.Read, FileShare.None);
                        }
                    });
                    var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                        NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                    if (revoke)
                    {
                        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                            service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                        Assert.AreEqual(1, denied, "Current denial must precede the later locked content read.");
                        Assert.IsNotNull(blocker);
                    }
                    else
                    {
                        TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                        Assert.IsTrue(plan.NoRestore);
                        foreach (var asset in assets)
                            CollectionAssert.Contains(plan.Context.ExecutionInputs.Select(input => input.FilePath).ToArray(), asset);
                    }
                    Assert.IsTrue(batchApproved, "Preserve complete assets preflight before individual reads.");
                    Assert.IsTrue(earlierProofReached, "Reach the earlier selector after the complete batch approval.");
                    Assert.HasCount(2, runner.Commands);
                    Assert.IsTrue(runner.Commands.All(command => command[0] == "restore"));
                    Assert.AreSequenceEqual(restoreTargets, runner.Commands.Select(command => command[1]).ToArray(), Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
                }
                catch (Exception failure) { primary = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Later assets blocker cleanup failed.", primary,
                        CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
                }

            }
            catch (Exception failure) { resourceCleanupFailure39 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure39, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure38 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure38, workspace); }
    }

    [TestMethod]
    [DataRow("early", false, false)]
    [DataRow("early", true, false)]
    [DataRow("terminal", false, false)]
    [DataRow("terminal", true, false)]
    [DataRow("capture", false, true)]
    [DataRow("capture", true, true)]
    [DataRow("terminal", false, true)]
    [DataRow("terminal", true, true)]
    public async Task NativePreparation_RechecksProtectedInputGrantAfterPackageCallbacks(string phase, bool revoke, bool packageCondition)
    {
        NuGetInstallationFixture fixture = await NuGetInstallationFixture.CreateAsync("Owned.ProtectedInput", "1.0.0",
            new Dictionary<string, byte[]> { ["marker.txt"] = "owned package payload"u8.ToArray() });
        Exception? resourceCleanupFailure40 = null;
        try
        {
            var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
            var marker = Path.Combine(fixture.Cache, "owned.protectedinput", "1.0.0", "marker.txt");
            var definition = System.Xml.Linq.XDocument.Load(projectPath);
            definition.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("IsTestProject", "true")));
            if (packageCondition)
            {
                definition.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup",
                    new System.Xml.Linq.XAttribute("Condition", "Exists('" + marker + "')"),
                    new System.Xml.Linq.XElement("OwnedMarkerPresent", "true")));
            }

            await File.WriteAllTextAsync(projectPath, definition.ToString(), TestContext.CancellationToken);
            var source = Path.Combine(fixture.Root, "Protected.cs");
            var sourceBytes = System.Text.Encoding.UTF8.GetBytes("class Protected { }");
            await File.WriteAllBytesAsync(source, sourceBytes, TestContext.CancellationToken);
            var protectedPath = packageCondition ? marker : source;
            if (packageCondition)
            {
                EvaluatedBuildGraph.Graph graph = EvaluatedBuildGraph.FromRoots([projectPath], fixture.LoadProperties,
                    System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
                Assert.Contains(probe => FileSystemPath.Comparer.Equals(probe.Path, marker), graph.Nodes.SelectMany(node => node.PathEvidence.ConditionProbes),
                    "The actual native evaluation must capture the custom-package condition target.");
                Assert.IsFalse(BuildInputProvenance.IsBootstrapPackagePath(marker),
                    "The owned custom cache must still require a current workspace grant.");
            }
            var archive = Path.Combine(fixture.Cache, "owned.protectedinput", "1.0.0", "owned.protectedinput.1.0.0.nupkg");
            var hash = archive + ".sha512";
            var metadata = Path.Combine(Path.GetDirectoryName(archive)!, ".nupkg.metadata");
            var status = new WorkspaceStatusDto("ws", projectPath, 1, "snapshot", DateTimeOffset.UtcNow, 1, 1,
                [new ProjectStatusDto("Owner", projectPath, 1, [], ["net10.0"], true, "Owner", "Library")], true, false, []);
            var workspace = MSBuildWorkspace.Create(fixture.LoadProperties);
            Exception? resourceCleanupFailure41 = null;
            try
            {
                var projectId = ProjectId.CreateNewId();
                Solution solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Owner", "Owner",
                    LanguageNames.CSharp, filePath: projectPath));
                FailClosedWorkspaceManagerStub manager = NativeManager(status, solution);
                var restored = false;
                var runner = new RecordingRunner { RestoreSucceeds = true, RestoreSideEffect = () => restored = true };
                var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
                Exception? resourceCleanupFailure42 = null;
                try
                {
                    var protectedPreflight = false;
                    var retainedArchiveObserved = false;
                    var creatingInstallationObserved = false;
                    var packageMarkerApproved = false;
                    var selectingAssetsAfterPackage = false;
                    var triggered = false;
                    var denied = 0;
                    FileStream? blocker = null;
                    Exception? primary = null;
                    try
                    {
                        var owned = FixturePolicy;
                        var policy = new CallbackInputPolicy(async (workspaceId, paths, ct) =>
                        {
                            if (triggered && revoke && paths.Contains(protectedPath, FileSystemPath.Comparer))
                            {
                                denied++;
                                throw new UnauthorizedAccessException("The protected input grant was revoked by later authorization.");
                            }
                            await owned.ValidateAsync(workspaceId, paths, ct);
                            if (phase == "capture")
                            {
                                if (triggered || restored || !paths.Contains(source, FileSystemPath.Comparer)
                                    || !paths.Contains(projectPath, FileSystemPath.Comparer)
                                    || paths.Contains(marker, FileSystemPath.Comparer))
                                {
                                    return;
                                }

                                protectedPreflight = true;
                                triggered = true;
                                if (revoke) blocker = new FileStream(protectedPath, FileMode.Open, FileAccess.Read, FileShare.None);
                                return;
                            }
                            if (!restored) return;
                            if (paths.Contains(source, FileSystemPath.Comparer)) protectedPreflight = true;
                            if (!protectedPreflight || triggered) return;
                            var retainedArchive = paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], archive);
                            var creatingInstallation = paths.Contains(archive, FileSystemPath.Comparer)
                                && paths.Contains(hash, FileSystemPath.Comparer) && paths.Contains(metadata, FileSystemPath.Comparer);
                            retainedArchiveObserved |= retainedArchive;
                            creatingInstallationObserved |= creatingInstallation;
                            packageMarkerApproved |= creatingInstallationObserved && paths.Count == 1
                                && FileSystemPath.Comparer.Equals(paths[0], marker);
                            selectingAssetsAfterPackage |= packageMarkerApproved && paths.Count == 1
                                && FileSystemPath.Comparer.Equals(paths[0], fixture.Assets);
                            if (packageCondition ? selectingAssetsAfterPackage : phase == "early" ? retainedArchive : creatingInstallation)
                            {
                                triggered = true;
                                if (revoke) blocker = new FileStream(protectedPath, FileMode.Open, FileAccess.Read, FileShare.None);
                            }
                        });
                        var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                            NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                        if (revoke)
                        {
                            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                                service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                            Assert.AreEqual(1, denied, "Current protected-input grant denial must precede the locked read.");
                            Assert.IsNotNull(blocker);
                        }
                        else
                        {
                            TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                            Assert.IsTrue(plan.NoRestore);
                            CollectionAssert.Contains(plan.Context.ExecutionInputs.Select(input => input.FilePath).ToArray(), source);
                            Assert.AreSequenceEqual(sourceBytes, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
                            if (packageCondition)
                            {
                                DotnetConfigurationIdentity markerIdentity = plan.Context.ExecutionInputs.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, marker));
                                Assert.IsTrue(markerIdentity.Exists);
                                Assert.AreSequenceEqual(System.Security.Cryptography.SHA256.HashData("owned package payload"u8), markerIdentity.ContentHash.ToArray());
                            }
                        }
                        Assert.AreEqual(phase != "capture" || !revoke, restored);
                        Assert.IsTrue(protectedPreflight, "The existing complete protected-input preflight must remain.");
                        if (phase != "capture")
                        {
                            Assert.IsTrue(retainedArchiveObserved, "The retained-package sweep must be reached before either package injection.");
                            Assert.AreEqual(phase == "terminal", creatingInstallationObserved,
                                "The terminal case must reach new package creation; early injection must precede it.");
                        }
                        Assert.IsTrue(triggered);
                        if (packageCondition && phase == "terminal")
                        {
                            Assert.IsTrue(packageMarkerApproved);
                            Assert.IsTrue(selectingAssetsAfterPackage,
                                "Revoke only at final selecting-assets validation, after the new package proof has finished reading its marker.");
                        }
                        Assert.HasCount(phase == "capture" && revoke ? 0 : 1, runner.Commands);
                        foreach (var command in runner.Commands) Assert.AreEqual("restore", command[0]);
                    }
                    catch (Exception failure) { primary = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Protected input blocker cleanup failed.", primary,
                            CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
                    }

                }
                catch (Exception failure) { resourceCleanupFailure42 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure42, executor); }

            }
            catch (Exception failure) { resourceCleanupFailure41 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure41, workspace); }

        }
        catch (Exception failure) { resourceCleanupFailure40 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure40, fixture); }
    }

    private sealed class CallbackInputPolicy(Func<string, IReadOnlyList<string>, CancellationToken, Task> validate) : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct) => validate(workspaceId, paths, ct);
    }

    private static FailClosedWorkspaceManagerStub NativeManager(WorkspaceStatusDto status, Solution solution) => new(status)
    {
        GetStatusAsyncHandler = _ => status,
        GetCurrentSolutionHandler = _ => solution,
        GetCurrentVersionHandler = _ => 1
    };

    [TestMethod]
    [DataRow("deny")]
    [DataRow("mutate-import")]
    [DataRow("freeze-assets")]
    public async Task NativePreparation_CoStorageSiblingDependencyAssetsRequireApprovalBeforeReadOrCommand(string mode)
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true);
        var path = status.Projects[0].FilePath;
        var dependency = Path.Combine(_fixtureRoot, "SiblingDependency.csproj");
        var dependencyAssets = Path.Combine(_fixtureRoot, "sibling-assets.json");
        var dependencyImport = Path.Combine(_fixtureRoot, "Sibling.props");
        await File.WriteAllTextAsync(dependencyImport, "<Project><PropertyGroup><DefineConstants>BEFORE_RESTORE</DefineConstants></PropertyGroup></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(dependency, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ProjectAssetsFile>sibling-assets.json</ProjectAssetsFile></PropertyGroup><Import Project=\"Sibling.props\" /></Project>", TestContext.CancellationToken);
        await File.WriteAllTextAsync(path, """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks>
            <IsTestProject>true</IsTestProject><IsTestingPlatformApplication>true</IsTestingPlatformApplication><OutputType>Exe</OutputType>
            <ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup>
            <ItemGroup><PackageReference Include="MSTest.TestFramework" Version="4.5.1" />
            <ProjectReference Include="SiblingDependency.csproj" Condition="'$(TargetFramework)' == 'net10.0'" /></ItemGroup></Project>
            """, TestContext.CancellationToken);
        await File.WriteAllTextAsync(dependencyAssets, "{}", TestContext.CancellationToken);
        var rootAssets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
        JsonNode json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(rootAssets, TestContext.CancellationToken))!;
        json["project"]!["frameworks"]!["net9.0"] = new System.Text.Json.Nodes.JsonObject();
        json["targets"]!["net9.0"] = json["targets"]!["net10.0"]!.DeepClone();
        await File.WriteAllTextAsync(rootAssets, json.ToJsonString(), TestContext.CancellationToken);
        Dictionary<string, string> properties = OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>());
        EvaluatedBuildGraph.Graph graph = EvaluatedBuildGraph.FromRoots([path], properties,
            System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
        EvaluatedBuildGraph.Node selected = graph.Nodes.Single(node => node.ProjectPath == path && node.TargetFramework == "net9.0");
        var workspace = MSBuildWorkspace.Create(properties);
        Exception? resourceCleanupFailure43 = null;
        try
        {
            var projectId = ProjectId.CreateNewId();
            Solution solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), status.Projects[0].Name,
                "Native", LanguageNames.CSharp, filePath: path, outputFilePath: selected.TargetPath));
            FailClosedWorkspaceManagerStub manager = NativeManager(status, solution);
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = mode == "mutate-import" ? () => File.WriteAllText(dependencyImport,
                    "<Project><PropertyGroup><DefineConstants>AFTER_RESTORE</DefineConstants></PropertyGroup></Project>") : null
            };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure44 = null;
            try
            {
                IWorkspacePathPolicy policy = mode == "deny" ? new DenyExactInputPolicy(dependencyAssets, FixturePolicy) : FixturePolicy;
                var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                FileStream? locked = mode == "deny" ? new FileStream(dependencyAssets, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
                Exception? resourceCleanupFailure45 = null;
                try
                {
                    if (mode == "deny")
                    {
                        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                        Assert.IsTrue(((DenyExactInputPolicy)policy).Refused, "The sibling-only dependency must be enrolled in the approval inventory.");
                        Assert.IsEmpty(runner.Commands);
                    }
                    else if (mode == "mutate-import")
                    {
                        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PrepareTestRunAsync("ws", null, null, CancellationToken.None));
                        Assert.HasCount(1, runner.Commands, "Changed authored imports must refuse before the second queued producer starts.");
                        Assert.AreEqual("restore", runner.Commands[0][0]);
                        Assert.Contains("AFTER_RESTORE", await File.ReadAllTextAsync(dependencyImport, TestContext.CancellationToken));
                    }
                    else
                    {
                        TestRunPlan plan = await service.PrepareTestRunAsync("ws", null, null, CancellationToken.None);
                        foreach (var input in new[] { dependency, dependencyImport, dependencyAssets })
                            CollectionAssert.Contains(plan.Context.ExecutionInputs.Select(item => item.FilePath).ToArray(), input);
                        await File.AppendAllTextAsync(dependencyAssets, " ", TestContext.CancellationToken);
                        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.RunTestCommandAsync(plan, CancellationToken.None));
                        Assert.HasCount(2, runner.Commands);
                    }

                }
                catch (Exception failure) { resourceCleanupFailure45 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure45, locked); }

            }
            catch (Exception failure) { resourceCleanupFailure44 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure44, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure43 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure43, workspace); }
    }

    private sealed class DenyExactInputPolicy(string denied, IWorkspacePathPolicy owned) : IWorkspacePathPolicy
    {
        public bool Refused { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Any(path => FileSystemPath.Comparer.Equals(path, denied)))
            {
                Refused = true;
                throw new UnauthorizedAccessException("The sibling dependency input is outside the current request authority.");
            }
            return owned.ValidateAsync(workspaceId, paths, ct);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(true)]
    public async Task WorkspaceAutoRestore_ApprovesAssetsBeforeAuthoritativeReadOrCommand(bool? autoRestore)
    {
        WorkspaceStatusDto status = await NativeFixtureAsync("MSTest.TestFramework", "4.5.1", report: true, capable: true);
        var workspace = CreateOwnedEvaluationWorkspace(status.Projects[0].FilePath);
        Exception? resourceCleanupFailure46 = null;
        try
        {
            Solution solution = SolutionFor(workspace, status);
            var assets = Path.Combine(_fixtureRoot, "obj", "project.assets.json");
            var denied = false;
            var manager = new FailClosedWorkspaceManagerStub(status)
            {
                GetStatusAsyncHandler = _ => status,
                GetCurrentSolutionHandler = _ => solution,
                AuthorizePathsHandler = (_, paths, _) =>
                {
                    Assert.Contains(assets, paths, FileSystemPath.Comparer);
                    denied = true;
                    throw new UnauthorizedAccessException("Current restore asset authority denied.");
                }
            };
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure47 = null;
            try
            {
                var locked = new FileStream(assets, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Exception? resourceCleanupFailure48 = null;
                try
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => RoslynMcp.Host.Stdio.Tools.WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                        executor, new ValidationServiceOptions(), manager, status with { RestoreRequired = true }, autoRestore, NullLogger.Instance, CancellationToken.None, System.TimeProvider.System));
                    Assert.IsTrue(denied);
                    Assert.IsEmpty(runner.Commands);

                }
                catch (Exception failure) { resourceCleanupFailure48 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure48, locked); }

            }
            catch (Exception failure) { resourceCleanupFailure47 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure47, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure46 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure46, workspace); }
    }

    private async Task<WorkspaceStatusDto> NativeFixtureAsync(string package, string version, bool report, bool capable)
    {
        WorkspaceStatusDto status = Status();
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "global.json"), "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}");
        await NativeSdkRestoreFixture.WriteProjectAsync(status.Projects[0].FilePath,
            $"<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsTestingPlatformApplication>{capable.ToString().ToLowerInvariant()}</IsTestingPlatformApplication><OutputType>Exe</OutputType><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile><MSBuildProjectExtensionsPath>obj</MSBuildProjectExtensionsPath></PropertyGroup><ItemGroup><PackageReference Include=\"{package}\" Version=\"{version}\" /></ItemGroup></Project>", TestContext.CancellationToken);
        var targets = new Dictionary<string, object> { [package + "/" + version] = new { type = "package" } };
        if (report) targets["Microsoft.Testing.Extensions.TrxReport/2.0.0"] = new { type = "package" };
        Directory.CreateDirectory(Path.Combine(_fixtureRoot, "obj"));
        await File.WriteAllTextAsync(Path.Combine(_fixtureRoot, "obj", "project.assets.json"), JsonSerializer.Serialize(new
        {
            project = new { frameworks = new Dictionary<string, object> { ["net10.0"] = new { } } },
            targets = new Dictionary<string, object> { ["net10.0"] = targets }
        }));
        return status;
    }

    [TestMethod]
    public async Task GatedExecution_RedactsUnknownPropertyFromArgumentsOutputAndLogs()
    {
        var runner = new RecordingRunner();
        var logger = new RecordingLogger();
        var executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, logger);
        Exception? resourceCleanupFailure49 = null;
        try
        {
            CommandExecutionDto result = await executor.ExecuteAsync("ws", "C:/probe.csproj", ["test", "-p:UnknownSetting=" + Encoded], TimeSpan.FromSeconds(5), CancellationToken.None);
            CollectionAssert.Contains(runner.Arguments.ToArray(), "-p:UnknownSetting=" + Encoded);
            AssertRedacted(JsonSerializer.Serialize(result));
            AssertRedacted(string.Join("\n", logger.Messages));

        }
        catch (Exception failure) { resourceCleanupFailure49 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure49, executor); }
    }

    [TestMethod]
    public async Task GatedExecution_RedactsFailureExceptionChain()
    {
        var runner = new RecordingRunner { Failure = new IOException(Sentinel, new InvalidOperationException(Encoded)) };
        var executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, NullLogger<GatedCommandExecutor>.Instance);
        Exception? resourceCleanupFailure50 = null;
        try
        {
            Exception exception = await Assert.ThrowsAsync<Exception>(() => executor.ExecuteAsync("ws", "C:/probe.csproj",
                ["test", "-p:UnknownSetting=" + Encoded], TimeSpan.FromSeconds(5), CancellationToken.None));
            AssertRedacted(exception.ToString());

        }
        catch (Exception failure) { resourceCleanupFailure50 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure50, executor); }
    }

    [TestMethod]
    public async Task GatedExecution_PreservesCancellationTokenWithoutDiagnosticLeakage()
    {
        var cancellation = new CancellationTokenSource();
        Exception? resourceCleanupFailure51 = null;
        try
        {
            var runner = new RecordingRunner { Failure = new OperationCanceledException(Sentinel, new IOException(Encoded), cancellation.Token) };
            var executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure52 = null;
            try
            {
                OperationCanceledException exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => executor.ExecuteAsync("ws", "C:/probe.csproj",
                    ["test", "-p:UnknownSetting=" + Encoded], TimeSpan.FromSeconds(5), CancellationToken.None));
                Assert.AreEqual(cancellation.Token, exception.CancellationToken);
                AssertRedacted(exception.ToString());

            }
            catch (Exception failure) { resourceCleanupFailure52 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure52, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure51 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure51, cancellation); }
    }

    [TestMethod]
    public async Task GatedExecution_CollectorFailure_RedactsDiagnosticPayload()
    {
        var executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), new RecordingRunner(), NullLogger<GatedCommandExecutor>.Instance);
        Exception? resourceCleanupFailure53 = null;
        try
        {
            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteTestAsync(
                "ws", "C:/probe.csproj", ["test", "-p:UnknownSetting=" + Encoded],
                (_, _) => throw new IOException(Sentinel, new InvalidOperationException(Encoded)),
                TimeSpan.FromSeconds(5), null, _ => Task.CompletedTask, CancellationToken.None));
            AssertRedacted(failure.ToString());
            StringAssert.Contains(failure.Message, nameof(IOException));

        }
        catch (Exception failure) { resourceCleanupFailure53 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure53, executor); }
    }

    [TestMethod]
    public async Task GatedExecution_CollectorSharesCommandTimeout()
    {
        var executor = new GatedCommandExecutor(new FailClosedWorkspaceManagerStub(), new RecordingRunner(), NullLogger<GatedCommandExecutor>.Instance);
        Exception? resourceCleanupFailure54 = null;
        try
        {
            CancellationToken collectorToken = default;
            var failure = await Assert.ThrowsExactlyAsync<TimeoutException>(() => executor.ExecuteTestAsync(
                "ws", "C:/probe.csproj", ["test", "-p:UnknownSetting=" + Encoded],
                async (_, token) =>
                {
                    collectorToken = token;
                    await Task.Delay(Timeout.Infinite, token);
                    throw new AssertFailedException("Timeout cancellation must interrupt collection.");
                }, TimeSpan.FromSeconds(3), null, _ => Task.CompletedTask, CancellationToken.None));
            Assert.IsTrue(collectorToken.IsCancellationRequested, "Collection must receive the executor's timeout token.");
            AssertRedacted(failure.ToString());

        }
        catch (Exception failure) { resourceCleanupFailure54 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure54, executor); }
    }

    [TestMethod]
    public async Task TestRun_ParsesInvocationTrxBeforeRedactingCommonValuesAndFailureStrings()
    {
        MsBuildInitializer.EnsureInitialized();
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string> { ["Configuration"] = "Debug", ["UnknownSetting"] = Sentinel });
        Exception? resourceCleanupFailure55 = null;
        try
        {
            WorkspaceStatusDto status = Status();
            Solution current = SolutionFor(workspace, status);
            var manager = new FailClosedWorkspaceManagerStub(status)
            {
                GetStatusAsyncHandler = _ => status,
                GetCurrentSolutionHandler = _ => current,
                GetCurrentVersionHandler = _ => 1
            };
            var report = $"""
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results><UnitTestResult testName="{Sentinel}" outcome="Failed"><Output><ErrorInfo>
                    <Message>{Sentinel}</Message><StackTrace>{Encoded}</StackTrace>
                  </ErrorInfo></Output></UnitTestResult></Results>
                  <ResultSummary outcome="Failed"><Counters total="1" passed="0" failed="1" notExecuted="0" /></ResultSummary>
                </TestRun>
                """;
            var runner = new RecordingRunner { RestoreSucceeds = true, ResultsContent = report };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure56 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor, NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                TestRunResultDto result = await service.RunTestsAsync("ws", null, null, CancellationToken.None);
                Assert.AreEqual(1, result.Total);
                Assert.AreEqual(1, result.Failed);
                Assert.HasCount(1, result.Failures);
                AssertRedacted(JsonSerializer.Serialize(result));

            }
            catch (Exception failure) { resourceCleanupFailure56 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure56, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure55 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure55, workspace); }
    }

    [TestMethod]
    public async Task BuildRun_PreservesDiagnosticParsingAndSpanIdentityBeforeRedaction()
    {
        var workspace = new AdhocWorkspace();
        Exception? resourceCleanupFailure57 = null;
        try
        {
            Project project = workspace.AddProject("BuildProbe", LanguageNames.CSharp).AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            var path = Path.Combine(TestTempRoot.Current, "Debug", "BuildProbe.cs");
            Document document = project.AddDocument("BuildProbe.cs", Microsoft.CodeAnalysis.Text.SourceText.From("class C { void M() { int unused; } }"), filePath: path);
            Diagnostic warning = (await document.Project.GetCompilationAsync(TestContext.CancellationToken))!.GetDiagnostics(TestContext.CancellationToken).Single(diagnostic => diagnostic.Id == "CS0168");
            LinePosition start = warning.Location.GetLineSpan().StartLinePosition;
            var manager = new FailClosedWorkspaceManagerStub
            {
                GetCurrentSolutionHandler = _ => document.Project.Solution,
                GetCurrentVersionHandler = _ => 1
            };
            var runner = new RecordingRunner { StdOutOverride = $"{path}({start.Line + 1},{start.Character + 1}): warning CS0168: {Sentinel} [C:/probe.csproj]" };
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure58 = null;
            try
            {
                var cache = new CompilationCache(manager);
                Exception? resourceCleanupFailure59 = null;
                try
                {
                    var service = new BuildService(manager, executor, cache, NullLogger<BuildService>.Instance);
                    var plan = new BuildCommandPlan("ws", "C:/probe.csproj",
                        ["build", "-p:Configuration=Debug", "-p:Collision=warning", "-p:UnknownSetting=" + Encoded], 1);
                    BuildCommandRun run = await service.RunBuildCommandAsync(plan, CancellationToken.None);
                    AssertRedacted(run.ToString());
                    AssertRedacted(JsonSerializer.Serialize(run));
                    BuildResultDto result = await service.CompleteBuildAsync(run, CancellationToken.None);
                    Assert.AreEqual(1, result.WarningCount);
                    Assert.AreEqual(0, result.ErrorCount);
                    Assert.HasCount(1, result.Diagnostics);
                    Assert.AreEqual("Warning", result.Diagnostics[0].Severity);
                    Assert.AreEqual("CS0168", result.Diagnostics[0].Id);
                    Assert.IsNotNull(result.Diagnostics[0].EndColumn, "The original physical identity must survive until span enrichment.");
                    AssertRedacted(JsonSerializer.Serialize(result));

                }
                catch (Exception failure) { resourceCleanupFailure59 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure59, cache); }

            }
            catch (Exception failure) { resourceCleanupFailure58 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure58, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure57 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure57, workspace); }
    }

    [TestMethod]
    [DataRow("-p:A=first-secret,B=second-secret", "first-secret")]
    [DataRow("/property:A=first-secret;B=second-secret", "first-secret")]
    [DataRow("--property:A=first-secret%3Bpart,B=second-secret", "first-secret;part")]
    [DataRow("-property:A=\"first-secret,part\";B=second-secret", "first-secret,part")]
    public void Redactor_HandlesMsBuildAssignmentSyntaxes(string argument, string firstValue)
    {
        var redacted = MsBuildCommandProperties.Redact(firstValue + " " + firstValue.Replace(";", "%3B", StringComparison.Ordinal) + " second-secret", [argument]);
        Assert.IsFalse(redacted.Contains(firstValue, StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("second-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PropertyArguments_PreserveRecordedPackageRootAndEscapeSeparators()
    {
        var properties = new Dictionary<string, string> { ["RestorePackagesPath"] = "configured-cache", ["Literal"] = "a,b;c%$(item)" };
        List<string> known = MsBuildCommandProperties.Append(["restore", "project.csproj", "--packages", "recorded-cache"], properties);
        Assert.DoesNotContain(argument => argument.StartsWith("-p:RestorePackagesPath=", StringComparison.Ordinal), known);
        CollectionAssert.Contains(known, "-p:Literal=a%2Cb%3Bc%25%24%28item%29");
        List<string> unknown = MsBuildCommandProperties.Append(["restore", "project.csproj"], properties);
        CollectionAssert.Contains(unknown, "-p:RestorePackagesPath=configured-cache");
    }

    [TestMethod]
    public void Redactor_PreservesAuthoredClassificationsAndWarningCodes()
    {
        var execution = new CommandExecutionDto("dotnet", [], "directory", "target", 1, false, 1, "Error Timeout", "Warning");
        var result = new TestRunResultDto(execution, 0, 0, 0, 0, [], new TestRunFailureEnvelopeDto("Timeout", false, "Timeout", "Error", "Warning"))
        { Warnings = [TestRunWarnings.WorkspaceChangedDuringRun] };
        TestRunResultDto redacted = MsBuildCommandProperties.Redact(result,
            ["-p:A=Error", "-p:B=Warning", "-p:C=Timeout", "-p:D=" + TestRunWarnings.WorkspaceChangedDuringRun]);
        Assert.AreEqual("Timeout", redacted.FailureEnvelope!.ErrorKind);
        Assert.AreSequenceEqual(result.Warnings.ToArray(), redacted.Warnings!.ToArray());
        Assert.AreEqual("[redacted]", redacted.FailureEnvelope.Summary);
    }

    [TestMethod]
    public void FailureRedaction_PreservesUnchangedDomainPolicyCancellationAndAggregateIdentity()
    {
        var redactor = new MsBuildCommandProperties.Redactor(["-p:UnknownSetting=" + Encoded]);
        var cancellation = new CancellationTokenSource();
        Exception? resourceCleanupFailure60 = null;
        try
        {
            var mismatch = new PhysicalGraphMismatchException("Evaluated inputs differ.");
            mismatch.Data["MetadataReferenceCounts"] = "2/1";
            var policy = new UnauthorizedAccessException("Scope refused.");
            var canceled = new OperationCanceledException(cancellation.Token);
            var aggregate = new AggregateException("Cleanup failed.", mismatch, policy);
            foreach (Exception failure in new Exception[] { mismatch, policy, canceled, aggregate })
                Assert.AreSame(failure, redactor.Failure(failure));

        }
        catch (Exception failure) { resourceCleanupFailure60 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure60, cancellation); }
    }

    [TestMethod]
    public void FailureRedaction_SanitizesTypedGraphDataAndCleanupHierarchy()
    {
        var redactor = new MsBuildCommandProperties.Redactor(["-p:UnknownSetting=" + Encoded]);
        var mismatch = new PhysicalGraphMismatchException(Sentinel);
        mismatch.Data["MissingMetadataFileNames"] = Encoded;
        mismatch.Data["MetadataReferenceCounts"] = "2/1";
        mismatch.Data[Sentinel] = Sentinel;
        mismatch.Data["Opaque"] = new { Value = Sentinel };
        var policy = new UnauthorizedAccessException(Sentinel, new IOException(Encoded));
        var observed = (AggregateException)redactor.Failure(new AggregateException("Cleanup: " + Sentinel, mismatch, policy));
        Assert.IsInstanceOfType<PhysicalGraphMismatchException>(observed.InnerExceptions[0]);
        Assert.IsInstanceOfType<UnauthorizedAccessException>(observed.InnerExceptions[1]);
        Assert.IsNull(observed.InnerExceptions[1].InnerException);
        IDictionary data = observed.InnerExceptions[0].Data;
        Assert.AreEqual("2/1", data["MetadataReferenceCounts"]);
        Assert.AreEqual("[redacted]", data["MissingMetadataFileNames"]);
        Assert.AreEqual("[unavailable]", data["Opaque"]);
        Assert.IsFalse(data.Contains(Sentinel));
        AssertRedacted(observed.ToString());
        AssertRedacted(string.Join(";", data.Cast<System.Collections.DictionaryEntry>().Select(entry => entry.Key + "=" + entry.Value)));
    }

    [TestMethod]
    public void FailureRedaction_SafeCleanupOutcomesPreserveExactCancellationIdentity()
    {
        var redactor = new MsBuildCommandProperties.Redactor(["-p:UnknownSetting=" + Encoded]);
        var cancellation = new CancellationTokenSource();
        Exception? resourceCleanupFailure61 = null;
        try
        {
            cancellation.Cancel();
            foreach (OperationCanceledException failure in new OperationCanceledException[]
            {
            new(cancellation.Token),
            new TaskCanceledException("Canceled.", null, cancellation.Token)
            })
            {
                OwnedResourceCleanup.AttachOutcomes(failure, ["close fork: IOException", "delete fork directory: UnauthorizedAccessException"]);
                Exception observed = redactor.Failure(failure);
                Assert.AreSame(failure, observed);
                Assert.AreEqual(cancellation.Token, ((OperationCanceledException)observed).CancellationToken);
                var original = failure.Data["CleanupOutcomes"] as string[];
                var retained = observed.Data["CleanupOutcomes"] as string[];
                Assert.IsNotNull(original);
                Assert.IsNotNull(retained);
                Assert.AreSequenceEqual(original, retained);
            }

        }
        catch (Exception failure) { resourceCleanupFailure61 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure61, cancellation); }
    }

    [TestMethod]
    public void FailureRedaction_CleanupOutcomeElementsAreRedactedAndBoundedWithTokenRetained()
    {
        var redactor = new MsBuildCommandProperties.Redactor(["-p:UnknownSetting=" + Encoded]);
        var cancellation = new CancellationTokenSource();
        Exception? resourceCleanupFailure62 = null;
        try
        {
            cancellation.Cancel();
            var failure = new OperationCanceledException(cancellation.Token);
            failure.Data["CleanupOutcomes"] = new[] { "delete fork: " + Sentinel, Encoded }
                .Concat(Enumerable.Repeat(new string('x', 180), 18)).ToArray();
            failure.Data["UntrustedArray"] = new[] { Sentinel };
            var observed = (OperationCanceledException)redactor.Failure(failure);
            Assert.AreEqual(cancellation.Token, observed.CancellationToken);
            var outcomes = observed.Data["CleanupOutcomes"] as string[];
            Assert.IsNotNull(outcomes);
            Assert.HasCount(16, outcomes);
            Assert.IsTrue(outcomes.All(outcome => outcome.Length <= 160));
            Assert.Contains("[redacted]", outcomes[0]);
            AssertRedacted(string.Join(";", outcomes));
            Assert.AreEqual("[unavailable]", observed.Data["UntrustedArray"]);

        }
        catch (Exception failure) { resourceCleanupFailure62 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure62, cancellation); }
    }

    private static MSBuildWorkspace CreateOwnedEvaluationWorkspace(string loadedPath)
    {
        MsBuildInitializer.EnsureInitialized();
        return MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(loadedPath, new Dictionary<string, string>()));
    }

    private static WorkspaceManager CreateOwnedManager(ForkInvocationPathPolicy policy)
    {
        var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        try
        {
            return new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 });
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native manager constructor watcher cleanup failed.", failure, watcher);
            throw;
        }
    }

    private IWorkspacePathPolicy? _fixturePolicy;
    private IWorkspacePathPolicy FixturePolicy => _fixturePolicy ?? throw new AssertFailedException("The native fixture configuration authority was not initialized.");
    private string _fixtureRoot = null!;
    [TestInitialize]
    public async Task InitializeFixture()
    {
        _fixtureRoot = Path.Combine(TestTempRoot.Current, nameof(TestRunnerEvaluationPropertiesTests), Guid.NewGuid().ToString("N"));
        try
        {
            NativeSdkRestoreFixture.InitializeDirectory(_fixtureRoot);
            NativeSdkRestoreFixture.WriteProject(Path.Combine(_fixtureRoot, "Tests.csproj"), "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
            var nativePolicy = await NativeSdkRestoreFixture.CreatePolicyAsync(Path.Combine(_fixtureRoot, "Tests.csproj"), TestContext.CancellationToken);
            _fixturePolicy = new CallbackInputPolicy(async (workspaceId, paths, ct) =>
            {
                try { await nativePolicy.ValidateAsync(workspaceId, paths, ct); }
                catch (UnauthorizedAccessException failure)
                {
                    var rejectedName = failure.Data["RejectedFileName"] as string;
                    var candidates = paths.Where(path => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) == rejectedName).ToArray();
                    TestContext.WriteLine("Native fixture policy refusal: name=" + rejectedName
                        + "; insideOwned=" + failure.Data["InsideOwnedRoot"]
                        + "; normalized=" + failure.Data["SameAfterDirectoryNormalization"]
                        + "; matchingCoordinates=" + candidates.Length + "; batchCount=" + paths.Count);
                    foreach (var path in candidates.Take(8)) TestContext.WriteLine("Native fixture refused coordinate: " + path);
                    throw;
                }
            });
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Evaluation fixture initialization cleanup failed.", failure,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(_fixtureRoot)));
            throw;
        }
    }
    [TestCleanup] public void CleanupFixture() => TestFixtureFileSystem.DeleteDirectoryIfExists(_fixtureRoot);

    private WorkspaceStatusDto Status() => new("ws", Path.Combine(_fixtureRoot, "Tests.csproj"), 1, "snapshot", DateTimeOffset.UtcNow, 1, 0,
        [new ProjectStatusDto("Tests", Path.Combine(_fixtureRoot, "Tests.csproj"), 0, [], ["net10.0"], true, "Tests", "Library")], true, false, []);

    private static Solution SolutionFor(Microsoft.CodeAnalysis.Workspace workspace, WorkspaceStatusDto status) => workspace.CurrentSolution.AddProject(
        ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Tests", "Tests", LanguageNames.CSharp, filePath: status.Projects[0].FilePath));

    private static void AssertRedacted(string text)
    {
        Assert.IsFalse(text.Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(Encoded, StringComparison.Ordinal));
    }

    private sealed class RecordingRunner : CannedCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public List<string[]> Commands { get; } = [];
        public bool RestoreSucceeds { get; init; }
        public Action? RestoreSideEffect { get; init; }
        public Exception? Failure { get; init; }
        public string? ResultsContent { get; init; }
        public string? StdOutOverride { get; init; }
        public override async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Arguments = arguments.ToArray();
            Commands.Add([.. arguments]);
            if (RestoreSucceeds && arguments[0] == "restore")
            {
                RestoreSideEffect?.Invoke();
                return new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, 0, true, 1, "", "");
            }
            if (Failure is not null) throw Failure;
            string? resultsPath = null;
            if (ResultsContent is not null)
            {
                var directory = arguments[arguments.ToList().IndexOf("--results-directory") + 1];
                resultsPath = Path.Combine(directory, "results.trx");
                await File.WriteAllTextAsync(resultsPath, ResultsContent, ct);
            }
            return new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, 1, false, 1,
                StdOutOverride ?? (resultsPath is null ? Sentinel : "Results File: " + resultsPath + "\n" + Sentinel), Encoded);
        }
    }

    private sealed class RecordingLogger : ILogger<GatedCommandExecutor>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class UnusedDiscovery : ITestDiscoveryService
    {
        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct) => throw new NotSupportedException();
        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) => throw new NotSupportedException();
    }

    public TestContext TestContext { get; set; }
}
