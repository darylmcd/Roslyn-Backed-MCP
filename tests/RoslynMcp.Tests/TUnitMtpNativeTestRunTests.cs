namespace RoslynMcp.Tests;

/// <summary>
/// tunit-mtp-native-test-run: TUnit is entirely built on Microsoft.Testing.Platform (MTP) and
/// never registers with the classic VSTest adapter, so test_run needs a different, MTP-native
/// dotnet test invocation for it. These fixtures deliberately never restore the TUnit package —
/// the two preconditions covered here (an unsupported filter, and no global.json opt-in) are
/// both checked and thrown before TestRunnerService ever shells out to dotnet test, so an
/// unrestorable/network-independent fixture is enough to exercise them.
/// </summary>
[TestClass]
public sealed class TUnitMtpNativeTestRunTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RunTestsAsync_TUnitProjectWithFilter_ThrowsBeforeExecutingDotnetTest()
    {
        var (workspaceId, projectName) = await LoadTUnitFixtureAsync(withGlobalJsonOptIn: true);
        try
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                TestRunnerService.RunTestsAsync(workspaceId, projectName, filter: "FullyQualifiedName~Foo", CancellationToken.None));

            StringAssert.Contains(ex.Message, "treenode-filter",
                "The error must point the caller at MTP's own filter syntax.");
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
}
