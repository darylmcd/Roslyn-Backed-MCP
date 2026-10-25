using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MsBuildMetadataLifetimeTests : TestBase
{
    private readonly List<string> _createdRoots = [];

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestCleanup]
    public async Task Cleanup() => await CleanupFailureCollector.DeleteDirectoriesAsync(_createdRoots);

    [TestMethod]
    public void EvaluateProperties_Success_PreservesGlobalsAndRequestedOrder()
    {
        var projectPath = CreateProject("""
            <Project><PropertyGroup><Probe>$(OperationValue)</Probe></PropertyGroup></Project>
            """);

        var values = MsBuildMetadataHelper.EvaluateProperties(
            projectPath, new Dictionary<string, string> { ["OperationValue"] = "operation" },
            "Probe", "Missing", "OperationValue");

        CollectionAssert.AreEqual(new[] { "operation", "", "operation" }, values);
    }

    [TestMethod]
    [DataRow("<Project><PropertyGroup>")]
    [DataRow("<Project><Import Project=\"Missing.props\" /></Project>")]
    public void EvaluateProperties_EvaluationFailure_PreservesExceptionClassification(string xml)
    {
        var projectPath = CreateProject(xml);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            MsBuildMetadataHelper.EvaluateProperties(
                projectPath, MsBuildMetadataHelper.DefaultGlobalProperties, "TargetFramework"));

        StringAssert.Contains(exception.Message, "MSBuild could not evaluate");
        Assert.AreEqual("Microsoft.Build.Exceptions.InvalidProjectFileException", exception.InnerException?.GetType().FullName);
    }

    [TestMethod]
    public async Task EvaluateProperty_FailedCacheLoad_PreservesNativeFailureAndReloadRecovery()
    {
        const string validProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Probe>recovered</Probe></PropertyGroup></Project>";
        var projectPath = CreateProject(validProject);
        var status = await WorkspaceManager.LoadAsync(projectPath, CancellationToken.None);
        try
        {
            File.WriteAllText(projectPath, "<Project><PropertyGroup>");
            var exception = await Assert.ThrowsAsync<Exception>(() => MsBuildEvaluationService.EvaluatePropertyAsync(
                status.WorkspaceId, "Probe", "Probe", CancellationToken.None));
            Assert.AreEqual("Microsoft.Build.Exceptions.InvalidProjectFileException", exception.GetType().FullName);

            File.WriteAllText(projectPath, validProject);
            await WorkspaceManager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
            var result = await MsBuildEvaluationService.EvaluatePropertyAsync(
                status.WorkspaceId, "Probe", "Probe", CancellationToken.None);
            Assert.AreEqual("recovered", result.EvaluatedValue);
        }
        finally
        {
            WorkspaceManager.Close(status.WorkspaceId);
        }
    }

    private string CreateProject(string xml)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(MsBuildMetadataLifetimeTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _createdRoots.Add(root);
        var projectPath = Path.Combine(root, "Probe.csproj");
        File.WriteAllText(projectPath, xml);
        return projectPath;
    }
}
