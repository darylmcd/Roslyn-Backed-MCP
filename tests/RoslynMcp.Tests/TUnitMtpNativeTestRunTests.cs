using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-mtp-native-test-run: TUnit is entirely built on Microsoft.Testing.Platform (MTP) and
/// never registers with the classic VSTest adapter, so test_run needs a different, MTP-native
/// dotnet test invocation for it. These fixtures deliberately never restore the TUnit package,
/// so most preconditions covered here (an untranslatable filter, no global.json opt-in, an
/// ambiguous multi-project target) are checked and thrown before TestRunnerService ever shells
/// out to dotnet test — an unrestorable/network-independent fixture is enough to exercise them.
/// The happy-path tests reach further: a <see cref="RecordingGatedCommandExecutor"/> fake stands
/// in for the real process launch, so the final argv itself is under test (this is what would
/// have caught the SDK 10.0.204 positional-path regression — every earlier test here stopped
/// before the executor and couldn't have). TreeNodeFilterTranslatorTests covers the
/// filter-translation grammar itself in isolation; the end-to-end behavior of a translated filter
/// against a real, restored TUnit project was verified manually (see TreeNodeFilterTranslator's
/// doc comment) rather than via a network-dependent committed test.
/// </summary>
[TestClass]
public sealed class TUnitMtpNativeTestRunTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RunTestsAsync_TUnitProjectWithUntranslatableFilter_ThrowsBeforeExecutingDotnetTest()
    {
        // "Foo" has no '.'-separated class/method for TreeNodeFilterTranslator to recover —
        // one of several untranslatable shapes; TreeNodeFilterTranslatorTests covers the rest.
        var (workspaceId, projectName) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                TestRunnerService.RunTestsAsync(workspaceId, projectName, filter: "FullyQualifiedName~Foo", CancellationToken.None));

            StringAssert.Contains(ex.Message, "class/method",
                "The error must explain why the filter didn't translate to MTP's --treenode-filter syntax.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_TUnitProjectDirectlyLoaded_ProjectNameOmitted_StillRoutesThroughMtpPlan()
    {
        // tunit-projectname-null-single-project-routing: a workspace loaded directly from one
        // .csproj (no solution) has exactly one candidate project, so an omitted projectName —
        // the normal single-project call shape — must route through the same MTP plan an
        // explicitly named project gets. Before this fix, this path silently built VSTest args
        // instead and never reached TreeNodeFilterTranslator at all.
        var (workspaceId, _) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                TestRunnerService.RunTestsAsync(workspaceId, projectName: null, filter: "FullyQualifiedName~Foo", CancellationToken.None));

            StringAssert.Contains(ex.Message, "class/method",
                "Reaching TreeNodeFilterTranslator's parse error — rather than silently shelling out with " +
                "VSTest args — proves the null-projectName path routed through the MTP plan.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_HappyPath_ProducesExpectedNativeMtpArgv()
    {
        // Item 7 / tunit-native-argv-requires-explicit-project-flag: the SDK 10.0.204 positional-
        // path repro slipped through review because every other test in this file stops before
        // the executor (an untranslatable filter, a missing global.json opt-in, or — for the
        // restore tests — Translate failing on an unrestored fixture). A fake executor lets this
        // one actually reach BuildMtpNativeArguments and assert on the exact final argv, without
        // touching the network or spawning a real dotnet process.
        var (workspaceId, _) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var recordingExecutor = new RecordingGatedCommandExecutor(restoreSucceeds: true);
            var service = new TestRunnerService(
                WorkspaceManager, recordingExecutor, NullLogger<TestRunnerService>.Instance);

            await service.RunTestsAsync(workspaceId, projectName: null, filter: null, CancellationToken.None);

            Assert.AreEqual(1, recordingExecutor.ExecutedArguments.Count);
            var argv = recordingExecutor.ExecutedArguments[0];
            Assert.AreEqual(6, argv.Count, $"Unexpected argv shape: [{string.Join(", ", argv)}]");
            Assert.AreEqual("test", argv[0]);
            Assert.AreEqual("--project", argv[1]);
            StringAssert.EndsWith(argv[2], "TUnitFixture.csproj");
            Assert.AreEqual("--report-trx", argv[3]);
            Assert.AreEqual("--results-directory", argv[4]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(argv[5]));
            Assert.IsFalse(argv.Contains("--no-restore"),
                "No filter was supplied, so the version-check restore path must not run.");
            Assert.IsFalse(argv.Contains("--treenode-filter"));
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_HappyPath_SingleAtomFilter_ProducesTranslatedTreeNodeFilterArgv()
    {
        // Item 7: a happy-path executor regression for the translated-filter shape, not just the
        // unfiltered argv above. A single atom (no '|') needs neither a resolved TUnit.Engine
        // version nor (with no ITestDiscoveryService wired here) discovery validation, so it
        // translates deterministically without a real restore.
        var (workspaceId, _) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var recordingExecutor = new RecordingGatedCommandExecutor(restoreSucceeds: true);
            var service = new TestRunnerService(
                WorkspaceManager, recordingExecutor, NullLogger<TestRunnerService>.Instance);

            await service.RunTestsAsync(
                workspaceId, projectName: null,
                filter: "FullyQualifiedName=MyNamespace.MyClass.MyMethod", CancellationToken.None);

            Assert.AreEqual(1, recordingExecutor.ExecutedArguments.Count);
            var argv = recordingExecutor.ExecutedArguments[0];
            var filterIndex = argv.ToList().IndexOf("--treenode-filter");
            Assert.AreNotEqual(-1, filterIndex, "--treenode-filter must be present in the final argv.");
            Assert.AreEqual("/*/MyNamespace/MyClass/MyMethod", argv[filterIndex + 1]);
            Assert.IsFalse(argv.Contains("--no-restore"), "A single atom's OR-version gate never applies.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_ProjectNameOmitted_MultiProjectWorkspaceContainingTUnit_ThrowsActionableRefusal()
    {
        // tunit-solution-level-mixed-mtp-refusal: a genuinely multi-project workspace can't
        // safely take the MTP branch for everything (Microsoft doesn't support mixing VSTest and
        // MTP projects in one dotnet test invocation), but silently staying on the classic
        // VSTest path would silently skip the TUnit project's tests. This must refuse instead.
        var workspaceId = await LoadMixedMultiProjectFixtureAsync();
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                TestRunnerService.RunTestsAsync(workspaceId, projectName: null, filter: null, CancellationToken.None));

            StringAssert.Contains(ex.Message, "Microsoft.Testing.Platform");
            StringAssert.Contains(ex.Message, "projectName");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_ProjectNameOmitted_OneProjectSolution_RoutesSoleTUnitProjectThroughMtpPlan()
    {
        await AssertSoleTUnitProjectRoutesThroughMtpAsync(includeApplicationProject: false);
    }

    [TestMethod]
    public async Task RunTestsAsync_ProjectNameOmitted_ApplicationAndOneTUnitProject_RoutesSoleTestProjectThroughMtpPlan()
    {
        await AssertSoleTUnitProjectRoutesThroughMtpAsync(includeApplicationProject: true);
    }

    [TestMethod]
    public async Task RunTestsAsync_OrFilter_RestoresBeforeCheckingResolvedVersion()
    {
        // tunit-treenode-filter-version-check-restore-snapshot: an OR filter's safety depends
        // on the resolved TUnit.Engine version, so an explicit restore must run BEFORE that
        // version is read -- otherwise a stale obj/project.assets.json could authorize (or wrongly
        // reject) the OR shape against a version that a subsequent implicit restore replaces.
        // A fake executor proves the ordering (and records the restore call) without touching the
        // network -- the real end-to-end restore+translate+execute path was verified manually
        // (see TestRunnerService's tunit-treenode-filter-version-check-restore-snapshot remarks).
        var (workspaceId, _) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var recordingExecutor = new RecordingGatedCommandExecutor(restoreSucceeds: true);
            var service = new TestRunnerService(
                WorkspaceManager, recordingExecutor, NullLogger<TestRunnerService>.Instance);

            // projectName: null relies on the directly-loaded-single-.csproj routing fix, which
            // avoids the fake executor needing to implement ResolveProject. The fixture is never
            // actually restored, so no real project.assets.json exists -- Translate still throws
            // past the restore step (unresolved TUnit.Engine version). That's fine: this test only
            // needs to prove restore ran first.
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.RunTestsAsync(
                    workspaceId, projectName: null,
                    filter: "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2",
                    CancellationToken.None));

            Assert.AreEqual(1, recordingExecutor.ExecutedArguments.Count);
            Assert.AreEqual("restore", recordingExecutor.ExecutedArguments[0][0]);
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_OrFilter_RestoreFails_ThrowsActionableErrorWithoutAttemptingDotnetTest()
    {
        var (workspaceId, _) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var recordingExecutor = new RecordingGatedCommandExecutor(restoreSucceeds: false);
            var service = new TestRunnerService(
                WorkspaceManager, recordingExecutor, NullLogger<TestRunnerService>.Instance);

            var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.RunTestsAsync(
                    workspaceId, projectName: null,
                    filter: "FullyQualifiedName~MyNamespace.MyClass.Method1|FullyQualifiedName~MyNamespace.MyClass.Method2",
                    CancellationToken.None));

            StringAssert.Contains(ex.Message, "restore");
            Assert.AreEqual(1, recordingExecutor.ExecutedArguments.Count,
                "A failed restore must not be followed by an attempt to run 'dotnet test'.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    private sealed class RecordingGatedCommandExecutor(bool restoreSucceeds) : IGatedCommandExecutor
    {
        public List<IReadOnlyList<string>> ExecutedArguments { get; } = [];

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
            => ExecuteAsync(workspaceId, targetPath, arguments, timeout, earlyKillPatterns: null, ct);

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
            CancellationToken ct)
        {
            ExecutedArguments.Add(arguments);
            var succeeded = !string.Equals(arguments[0], "restore", StringComparison.Ordinal) || restoreSucceeds;
            return Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: Path.GetDirectoryName(targetPath) ?? ".",
                TargetPath: targetPath,
                ExitCode: succeeded ? 0 : 1,
                Succeeded: succeeded,
                DurationMs: 1,
                StdOut: string.Empty,
                StdErr: string.Empty));
        }

        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public void Dispose()
        {
        }
    }

    [TestMethod]
    public async Task RunTestsAsync_TUnitProjectWithoutGlobalJsonOptIn_ThrowsActionableError()
    {
        // tunit-legacy-vstest-bridge-removed-net10: verified by direct repro against a real
        // TUnit project — on the .NET 10 SDK, "dotnet test -p:TestingPlatformDotnetTestSupport=true
        // -- --report-trx" (the legacy VSTest-mode MTP bridge) fails hard with "Testing with
        // VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and
        // later." There is no fallback argument shape to attempt without the global.json opt-in.
        var (workspaceId, projectName) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: false);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                TestRunnerService.RunTestsAsync(workspaceId, projectName, filter: null, CancellationToken.None));

            StringAssert.Contains(ex.Message, "global.json");
            StringAssert.Contains(ex.Message, "Microsoft.Testing.Platform");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    private static async Task<(string WorkspaceId, string ProjectName)> LoadTUnitFixtureAsync(bool withGlobalJsonOptIn)
    {
        const string projectName = "TUnitFixture";
        var root = Path.Combine(TestTempRoot.Current, nameof(TUnitMtpNativeTestRunTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var projectPath = Path.Combine(root, $"{projectName}.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.65.38" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.cs"), "// MTP generates its own entry point.\n");

        if (withGlobalJsonOptIn)
        {
            File.WriteAllText(
                Path.Combine(root, "global.json"),
                """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
        }

        var loaded = await WorkspaceManager.LoadAsync(projectPath, CancellationToken.None);
        return (loaded.WorkspaceId, projectName);
    }

    private static async Task<string> LoadMixedMultiProjectFixtureAsync()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TUnitMtpNativeTestRunTests), "mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var mstestDir = Path.Combine(root, "MsTestProject");
        Directory.CreateDirectory(mstestDir);
        File.WriteAllText(
            Path.Combine(mstestDir, "MsTestProject.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(mstestDir, "Placeholder.cs"), "namespace MsTestProject;\n");

        var tunitDir = Path.Combine(root, "TUnitProject");
        Directory.CreateDirectory(tunitDir);
        File.WriteAllText(
            Path.Combine(tunitDir, "TUnitProject.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.65.38" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(tunitDir, "Program.cs"), "// MTP generates its own entry point.\n");

        File.WriteAllText(
            Path.Combine(root, "Mixed.slnx"),
            """
            <Solution>
              <Project Path="MsTestProject/MsTestProject.csproj" />
              <Project Path="TUnitProject/TUnitProject.csproj" />
            </Solution>
            """);

        var loaded = await WorkspaceManager.LoadAsync(Path.Combine(root, "Mixed.slnx"), CancellationToken.None);
        return loaded.WorkspaceId;
    }

    private static async Task AssertSoleTUnitProjectRoutesThroughMtpAsync(bool includeApplicationProject)
    {
        var workspaceId = await LoadSoleTUnitProjectSolutionFixtureAsync(includeApplicationProject);
        try
        {
            var recordingExecutor = new RecordingGatedCommandExecutor(restoreSucceeds: true);
            var service = new TestRunnerService(
                WorkspaceManager, recordingExecutor, NullLogger<TestRunnerService>.Instance);

            await service.RunTestsAsync(workspaceId, projectName: null, filter: null, CancellationToken.None);

            Assert.AreEqual(1, recordingExecutor.ExecutedArguments.Count);
            var argv = recordingExecutor.ExecutedArguments[0];
            var projectArgumentIndex = argv.ToList().IndexOf("--project");
            Assert.AreNotEqual(-1, projectArgumentIndex, "The sole TUnit project must use MTP's --project argument.");
            StringAssert.EndsWith(argv[projectArgumentIndex + 1], "TUnitProject.csproj");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    private static async Task<string> LoadSoleTUnitProjectSolutionFixtureAsync(bool includeApplicationProject)
    {
        var root = Path.Combine(
            TestTempRoot.Current,
            nameof(TUnitMtpNativeTestRunTests),
            "sole-tunit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "global.json"),
            """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");

        var tunitDir = Path.Combine(root, "TUnitProject");
        Directory.CreateDirectory(tunitDir);
        File.WriteAllText(
            Path.Combine(tunitDir, "TUnitProject.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.65.38" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(tunitDir, "Program.cs"), "// MTP generates its own entry point.\n");

        var applicationEntry = string.Empty;
        if (includeApplicationProject)
        {
            var applicationDir = Path.Combine(root, "Application");
            Directory.CreateDirectory(applicationDir);
            File.WriteAllText(
                Path.Combine(applicationDir, "Application.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(applicationDir, "Placeholder.cs"), "namespace Application;\n");
            applicationEntry = "  <Project Path=\"Application/Application.csproj\" />\n";
        }

        File.WriteAllText(
            Path.Combine(root, "SoleTUnit.slnx"),
            $"<Solution>\n{applicationEntry}  <Project Path=\"TUnitProject/TUnitProject.csproj\" />\n</Solution>\n");

        var loaded = await WorkspaceManager.LoadAsync(Path.Combine(root, "SoleTUnit.slnx"), CancellationToken.None);
        return loaded.WorkspaceId;
    }
}
