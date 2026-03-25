using System.Xml.Linq;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectRefactoringIntegrationTests : TestBase
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
    public async Task Extract_Interface_Preview_And_Apply_Creates_Interface_File_And_Project_Reference()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            AddProjectToCopiedSolution(copiedRoot, "Contracts", "net10.0");

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var sourceFilePath = Path.Combine(copiedRoot, "SampleLib", "AnimalService.cs");
            var sourceProjectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");
            var interfaceFilePath = Path.Combine(copiedRoot, "Contracts", "IAnimalService.cs");

            var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                workspaceId,
                sourceFilePath,
                "AnimalService",
                "IAnimalService",
                "Contracts",
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsTrue(File.Exists(interfaceFilePath));

            var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);
            Assert.IsTrue(sourceContents.Contains("IAnimalService", StringComparison.Ordinal));

            var projectXml = XDocument.Load(sourceProjectFilePath);
            Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
                string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "Contracts.csproj", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Move_Type_To_Project_Preview_And_Apply_Moves_File_And_Adds_Project_Reference()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            AddProjectToCopiedSolution(copiedRoot, "AnimalsShared", "net10.0");

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var sourceFilePath = Path.Combine(copiedRoot, "SampleLib", "Dog.cs");
            var targetFilePath = Path.Combine(copiedRoot, "AnimalsShared", "Dog.cs");
            var sourceProjectFilePath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");

            var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
                workspaceId,
                sourceFilePath,
                "Dog",
                "AnimalsShared",
                null,
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsFalse(File.Exists(sourceFilePath));
            Assert.IsTrue(File.Exists(targetFilePath));

            var projectXml = XDocument.Load(sourceProjectFilePath);
            Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
                string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "AnimalsShared.csproj", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Dependency_Inversion_Preview_And_Apply_Rewrites_Constructor_Parameters()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            AddProjectToCopiedSolution(copiedRoot, "Contracts", "net10.0");
            var consumerFilePath = Path.Combine(copiedRoot, "SampleApp", "AnimalCoordinator.cs");
            File.WriteAllText(
                consumerFilePath,
                "using SampleLib;\n\npublic class AnimalCoordinator\n{\n    public AnimalCoordinator(AnimalService service)\n    {\n    }\n}\n");

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;
            var sourceFilePath = Path.Combine(copiedRoot, "SampleLib", "AnimalService.cs");
            var interfaceFilePath = Path.Combine(copiedRoot, "Contracts", "IAnimalService.cs");

            var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
                workspaceId,
                sourceFilePath,
                "AnimalService",
                "IAnimalService",
                "Contracts",
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, applyResult.Error);
            Assert.IsTrue(File.Exists(interfaceFilePath));

            var consumerContents = await File.ReadAllTextAsync(consumerFilePath, CancellationToken.None);
            Assert.IsTrue(consumerContents.Contains("IAnimalService", StringComparison.Ordinal));
            Assert.IsFalse(consumerContents.Contains("AnimalService service", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
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