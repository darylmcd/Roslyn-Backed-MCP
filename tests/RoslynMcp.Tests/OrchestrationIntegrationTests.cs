using System.Xml.Linq;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OrchestrationIntegrationTests : IsolatedWorkspaceTestBase
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
    public async Task Migrate_Package_Preview_And_Apply_Updates_Project_Files_And_Central_Package_Versions()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sampleLibProject = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var sampleAppProject = workspace.GetPath("SampleApp", "SampleApp.csproj");
        InjectPackageReference(sampleLibProject, "Legacy.Package");
        InjectPackageReference(sampleAppProject, "Legacy.Package");
        InjectCentralPackageVersion(workspace.GetPath("Directory.Packages.props"), "Legacy.Package", "1.0.0");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await OrchestrationService.PreviewMigratePackageAsync(
            workspace.WorkspaceId,
            "Legacy.Package",
            "Modern.Package",
            "2.5.0",
            CancellationToken.None);

        var applyResult = await OrchestrationService.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var libXml = XDocument.Load(sampleLibProject);
        var appXml = XDocument.Load(sampleAppProject);
        var packagesXml = XDocument.Load(workspace.GetPath("Directory.Packages.props"));

        Assert.IsFalse(libXml.Descendants("PackageReference").Any(element => string.Equals((string?)element.Attribute("Include"), "Legacy.Package", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(libXml.Descendants("PackageReference").Any(element => string.Equals((string?)element.Attribute("Include"), "Modern.Package", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(appXml.Descendants("PackageReference").Any(element => string.Equals((string?)element.Attribute("Include"), "Modern.Package", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(packagesXml.Descendants("PackageVersion").Any(element =>
            string.Equals((string?)element.Attribute("Include"), "Modern.Package", StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string?)element.Attribute("Version"), "2.5.0", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Split_Class_Preview_And_Apply_Creates_New_Partial_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var newFilePath = workspace.GetPath("SampleLib", "Dog.Behavior.cs");

        var preview = await OrchestrationService.PreviewSplitClassAsync(
            workspace.WorkspaceId,
            dogFilePath,
            "Dog",
            ["Fetch"],
            "Dog.Behavior.cs",
            CancellationToken.None);

        var applyResult = await OrchestrationService.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(newFilePath));

        var originalContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var newContents = await File.ReadAllTextAsync(newFilePath, CancellationToken.None);

        Assert.IsFalse(originalContents.Contains("void Fetch", StringComparison.Ordinal));
        Assert.IsTrue(newContents.Contains("void Fetch", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Extract_And_Wire_Interface_Preview_And_Apply_Rewrites_Di_Registration()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var appProjectFile = workspace.GetPath("SampleApp", "SampleApp.csproj");
        InjectProjectReference(appProjectFile, "..\\SampleLib\\SampleLib.csproj");
        var registrationsFile = workspace.GetPath("SampleApp", "ServiceRegistration.cs");
        File.WriteAllText(
            registrationsFile,
            "using Microsoft.Extensions.DependencyInjection;\nusing SampleLib;\n\npublic static class ServiceRegistration\n{\n    public static void AddServices(IServiceCollection services)\n    {\n        services.AddSingleton<AnimalService>();\n    }\n}\n");

        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await OrchestrationService.PreviewExtractAndWireInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            updateDiRegistrations: true,
            CancellationToken.None);

        var applyResult = await OrchestrationService.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(interfaceFilePath));

        var registrationsContents = await File.ReadAllTextAsync(registrationsFile, CancellationToken.None);
        Assert.IsTrue(registrationsContents.Contains("AddSingleton<IAnimalService, AnimalService>()", StringComparison.Ordinal));
    }

    private static void InjectPackageReference(string projectFilePath, string packageId)
    {
        var projectXml = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        var itemGroup = projectXml.Root?.Elements("ItemGroup").FirstOrDefault(element => element.Elements("PackageReference").Any())
            ?? new XElement("ItemGroup");
        if (itemGroup.Parent is null)
        {
            projectXml.Root?.Add(itemGroup);
        }

        itemGroup.Add(new XElement("PackageReference", new XAttribute("Include", packageId)));
        projectXml.Save(projectFilePath, SaveOptions.DisableFormatting);
    }

    private static void InjectCentralPackageVersion(string packagesPropsPath, string packageId, string version)
    {
        var propsXml = XDocument.Load(packagesPropsPath, LoadOptions.PreserveWhitespace);
        var itemGroup = propsXml.Root?.Elements("ItemGroup").FirstOrDefault(element => element.Elements("PackageVersion").Any())
            ?? new XElement("ItemGroup");
        if (itemGroup.Parent is null)
        {
            propsXml.Root?.Add(itemGroup);
        }

        itemGroup.Add(new XElement("PackageVersion", new XAttribute("Include", packageId), new XAttribute("Version", version)));
        propsXml.Save(packagesPropsPath, SaveOptions.DisableFormatting);
    }

    private static void InjectProjectReference(string projectFilePath, string relativeProjectPath)
    {
        var projectXml = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        var itemGroup = projectXml.Root?.Elements("ItemGroup").FirstOrDefault(element => element.Elements("ProjectReference").Any())
            ?? new XElement("ItemGroup");
        if (itemGroup.Parent is null)
        {
            projectXml.Root?.Add(itemGroup);
        }

        itemGroup.Add(new XElement("ProjectReference", new XAttribute("Include", relativeProjectPath)));
        projectXml.Save(projectFilePath, SaveOptions.DisableFormatting);
    }

    private static void AddProjectToCopiedSolution(string copiedRoot, string projectName, string targetFramework)
    {
        var projectDirectory = Path.Combine(copiedRoot, projectName);
        Directory.CreateDirectory(projectDirectory);

        var projectFilePath = Path.Combine(projectDirectory, projectName + ".csproj");
        File.WriteAllText(projectFilePath, $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>{targetFramework}</TargetFramework>\n    <Nullable>enable</Nullable>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");

        var solutionFilePath = Path.Combine(copiedRoot, "SampleSolution.slnx");
        var solutionDocument = XDocument.Load(solutionFilePath, LoadOptions.PreserveWhitespace);
        solutionDocument.Root?.Add(new XElement("Project", new XAttribute("Path", $"{projectName}/{projectName}.csproj")));
        solutionDocument.Save(solutionFilePath, SaveOptions.DisableFormatting);
    }
}
