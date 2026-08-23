namespace RoslynMcp.Tests;

/// <summary>
/// tunit-mtp-native-test-run: TUnit is entirely built on Microsoft.Testing.Platform (MTP) and
/// never registers with the classic VSTest adapter, so test_run needs a different, MTP-native
/// dotnet test invocation for it. These fixtures deliberately never restore the TUnit package —
/// every precondition covered here (an untranslatable filter, and no global.json opt-in) is
/// checked and thrown before TestRunnerService ever shells out to dotnet test, so an
/// unrestorable/network-independent fixture is enough to exercise them. TreeNodeFilterTranslatorTests
/// covers the filter-translation grammar itself in isolation; the end-to-end behavior of a
/// translated filter against a real, restored TUnit project was verified manually (see
/// TreeNodeFilterTranslator's doc comment) rather than via a network-dependent committed test.
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
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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
    public async Task RunTestsAsync_ProjectNameOmitted_MultiProjectWorkspaceContainingTUnit_ThrowsActionableRefusal()
    {
        // tunit-solution-level-mixed-mtp-refusal: a genuinely multi-project workspace can't
        // safely take the MTP branch for everything (Microsoft doesn't support mixing VSTest and
        // MTP projects in one dotnet test invocation), but silently staying on the classic
        // VSTest path would silently skip the TUnit project's tests. This must refuse instead.
        var workspaceId = await LoadMixedMultiProjectFixtureAsync();
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
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
}
