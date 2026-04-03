using System.Xml.Linq;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ProjectMutationIntegrationTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Add_Package_Reference_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var projectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");

            var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
                workspaceId,
                new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
                CancellationToken.None);

            Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase)));

            var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var projectXml = XDocument.Load(projectFilePath);
            Assert.IsTrue(projectXml.Descendants("PackageReference").Any(element =>
                string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Add_Project_Reference_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var projectFilePath = Path.Combine(copiedRoot, "SampleApp", "SampleApp.csproj");

            var preview = await ProjectMutationService.PreviewAddProjectReferenceAsync(
                workspaceId,
                new AddProjectReferenceDto("SampleApp", "SampleLib.Tests"),
                CancellationToken.None);

            Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase)));

            var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var projectXml = XDocument.Load(projectFilePath);
            Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
                string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "SampleLib.Tests.csproj", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Set_Project_Property_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var projectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");

            var preview = await ProjectMutationService.PreviewSetProjectPropertyAsync(
                workspaceId,
                new SetProjectPropertyDto("SampleLib", "LangVersion", "preview"),
                CancellationToken.None);

            var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var projectXml = XDocument.Load(projectFilePath);
            Assert.AreEqual("preview", projectXml.Descendants("LangVersion").First().Value);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Add_And_Remove_Target_Framework_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var projectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");

            var addPreview = await ProjectMutationService.PreviewAddTargetFrameworkAsync(
                workspaceId,
                new AddTargetFrameworkDto("SampleLib", "net8.0"),
                CancellationToken.None);

            var addResult = await ProjectMutationService.ApplyProjectMutationAsync(addPreview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(addResult.Success, addResult.Error);

            var projectXml = XDocument.Load(projectFilePath);
            Assert.AreEqual("net10.0;net8.0", projectXml.Descendants("TargetFrameworks").First().Value);

            var removePreview = await ProjectMutationService.PreviewRemoveTargetFrameworkAsync(
                workspaceId,
                new RemoveTargetFrameworkDto("SampleLib", "net8.0"),
                CancellationToken.None);

            var removeResult = await ProjectMutationService.ApplyProjectMutationAsync(removePreview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(removeResult.Success, removeResult.Error);

            projectXml = XDocument.Load(projectFilePath);
            Assert.AreEqual("net10.0", projectXml.Descendants("TargetFramework").First().Value);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Set_Conditional_Property_Preview_And_Apply_Adds_Conditional_Property_Group()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var projectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");
            const string condition = "'$(Configuration)'=='Release'";

            var preview = await ProjectMutationService.PreviewSetConditionalPropertyAsync(
                workspaceId,
                new SetConditionalPropertyDto("SampleLib", "LangVersion", "preview", condition),
                CancellationToken.None);

            var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var projectXml = XDocument.Load(projectFilePath);
            var propertyGroup = projectXml.Root?.Elements("PropertyGroup")
                .FirstOrDefault(element => string.Equals((string?)element.Attribute("Condition"), condition, StringComparison.Ordinal));

            Assert.IsNotNull(propertyGroup);
            Assert.AreEqual("preview", propertyGroup.Element("LangVersion")?.Value);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Add_And_Remove_Central_Package_Version_Preview_And_Apply_Updates_Directory_Packages_Props()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var packagesPropsPath = Path.Combine(copiedRoot, "Directory.Packages.props");

            var addPreview = await ProjectMutationService.PreviewAddCentralPackageVersionAsync(
                workspaceId,
                new AddCentralPackageVersionDto("Humanizer.Core", "2.14.1"),
                CancellationToken.None);

            var addResult = await ProjectMutationService.ApplyProjectMutationAsync(addPreview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(addResult.Success, addResult.Error);

            var propsXml = XDocument.Load(packagesPropsPath);
            Assert.IsTrue(propsXml.Descendants("PackageVersion").Any(element =>
                string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("Version"), "2.14.1", StringComparison.OrdinalIgnoreCase)));

            var removePreview = await ProjectMutationService.PreviewRemoveCentralPackageVersionAsync(
                workspaceId,
                new RemoveCentralPackageVersionDto("Humanizer.Core"),
                CancellationToken.None);

            var removeResult = await ProjectMutationService.ApplyProjectMutationAsync(removePreview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(removeResult.Success, removeResult.Error);

            propsXml = XDocument.Load(packagesPropsPath);
            Assert.IsFalse(propsXml.Descendants("PackageVersion").Any(element =>
                string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Add_Package_Reference_Preview_When_Central_Package_Management_Is_Active_Omits_Version_And_Returns_Warning()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var projectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");

            var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
                workspaceId,
                new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
                CancellationToken.None);

            CollectionAssert.Contains(preview.Warnings?.ToArray() ?? [], "Central package management is enabled. Add 'Humanizer.Core' to Directory.Packages.props before building or applying a central package version preview.");

            var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);

            var projectXml = XDocument.Load(projectFilePath);
            var packageReference = projectXml.Descendants("PackageReference").First(element =>
                string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase));

            Assert.IsNull(packageReference.Attribute("Version"));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
