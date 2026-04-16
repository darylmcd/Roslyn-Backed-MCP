using System.Xml.Linq;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectRefactoringIntegrationTests : IsolatedWorkspaceTestBase
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
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var sourceProjectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
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

    [TestMethod]
    public async Task Move_Type_To_Project_Preview_And_Apply_Moves_File_And_Adds_Project_Reference()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "AnimalsShared", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var targetFilePath = workspace.GetPath("AnimalsShared", "Dog.cs");
        var sourceProjectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "Dog",
            "AnimalsShared",
            null,
            CancellationToken.None,
            preserveNamespace: false);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsFalse(File.Exists(sourceFilePath));
        Assert.IsTrue(File.Exists(targetFilePath));

        var movedText = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(movedText, "namespace AnimalsShared");

        var projectXml = XDocument.Load(sourceProjectFilePath);
        Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
            string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "AnimalsShared.csproj", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Move_Type_To_Project_PreserveNamespace_Keeps_Source_Namespace()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "AnimalsShared", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var targetFilePath = workspace.GetPath("AnimalsShared", "Dog.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "Dog",
            "AnimalsShared",
            null,
            CancellationToken.None,
            preserveNamespace: true);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        var movedText = await File.ReadAllTextAsync(targetFilePath, CancellationToken.None);
        StringAssert.Contains(movedText, "namespace SampleLib");
    }

    [TestMethod]
    public async Task Dependency_Inversion_Preview_And_Apply_Rewrites_Constructor_Parameters()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var consumerFilePath = workspace.GetPath("SampleApp", "AnimalCoordinator.cs");
        File.WriteAllText(
            consumerFilePath,
            "using SampleLib;\n\npublic class AnimalCoordinator\n{\n    public AnimalCoordinator(AnimalService service)\n    {\n    }\n}\n");

        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
            workspace.WorkspaceId,
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

    [TestMethod]
    public async Task Extract_Interface_Preview_Generates_Formatted_Interface_File_Across_Projects()
    {
        // Regression for dr-9-2-format-bug-001-cross-project-interface-extractio.
        // Before the fix: the generated interface file read
        //     `publicinterfaceIAnimalService{...}`
        // with every token glued together, and the source class's base list emitted
        //     `public class AnimalService\n : IAnimalService{`
        // with `{` glued to the interface name. After the fix both shapes are readable C#.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var interfaceFilePath = workspace.GetPath("Contracts", "IAnimalService.cs");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var interfaceText = await File.ReadAllTextAsync(interfaceFilePath, CancellationToken.None);

        // --- Interface file whitespace assertions ---
        // (1) Keywords and identifiers must be separated by whitespace.
        Assert.IsFalse(
            interfaceText.Contains("publicinterface", StringComparison.Ordinal),
            $"Interface file has glued-together tokens (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");
        Assert.IsFalse(
            interfaceText.Contains("interfaceIAnimalService", StringComparison.Ordinal),
            $"Interface file has glued-together tokens (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");
        StringAssert.Contains(interfaceText, "public interface IAnimalService");

        // (2) Opening brace must be on a line following the declaration (either same-line with a
        //     preceding space or on its own line).
        Assert.IsFalse(
            interfaceText.Contains("IAnimalService{", StringComparison.Ordinal),
            $"Interface file has opening brace glued to declaration identifier (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");

        // (3) File must span multiple lines (reformatted output, not a one-liner).
        var lineCount = interfaceText.Split('\n').Length;
        Assert.IsTrue(
            lineCount >= 4,
            $"Interface file was emitted on {lineCount} line(s); expected at least 4 for a formatted file.\nFile contents:\n{interfaceText}");

        // (4) Parameter lists and method signatures must have whitespace preserved.
        Assert.IsFalse(
            interfaceText.Contains("IEnumerable<IAnimal>animals", StringComparison.Ordinal),
            $"Interface file has glued-together parameter type and name (FORMAT-BUG-001 regression).\nFile contents:\n{interfaceText}");

        // --- Source file base-list assertions ---
        var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);
        Assert.IsTrue(
            sourceContents.Contains("IAnimalService", StringComparison.Ordinal),
            "Source class should declare the new interface in its base list.");
        // The `{` of the class body must not be glued onto the interface name.
        Assert.IsFalse(
            sourceContents.Contains("IAnimalService{", StringComparison.Ordinal),
            $"Source class has `{{` glued onto the interface base type (FORMAT-BUG-001 regression).\nFile contents:\n{sourceContents}");
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
