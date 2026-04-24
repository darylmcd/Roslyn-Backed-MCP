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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(interfaceFilePath));

        var consumerContents = await File.ReadAllTextAsync(consumerFilePath, CancellationToken.None);
        Assert.IsTrue(consumerContents.Contains("IAnimalService", StringComparison.Ordinal));
        // Constructor parameter must now declare the interface, not the concrete class. Use a
        // word-boundary-aware check so `IAnimalService service` (the desired post-fix output)
        // doesn't trip a naive "AnimalService service" substring match.
        Assert.IsFalse(
            consumerContents.Contains("(AnimalService service", StringComparison.Ordinal),
            $"Constructor still declares concrete type.\nConsumer contents:\n{consumerContents}");
        StringAssert.Contains(consumerContents, "IAnimalService service");
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

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
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

    [TestMethod]
    public async Task Dependency_Inversion_Preview_Preserves_Source_File_Formatting()
    {
        // Regression for dr-9-3-format-bug-002-destroys-source-formatting (FORMAT-BUG-002).
        // Before the fix: CreateInterfaceExtractionSolutionAsync called
        //     updatedSourceRoot = ((CompilationUnitSyntax)updatedSourceRoot).NormalizeWhitespace();
        // which re-flowed the ENTIRE source compilation unit — collapsed intentional spacing,
        // dropped blank lines, reshuffled indentation. After the fix the source file's original
        // trivia is preserved; only the targeted `: IName` edit is applied.
        //
        // Seed the source file with distinctive whitespace (multiple spaces inside a parameter
        // list, blank lines between members, trailing whitespace) so NormalizeWhitespace would
        // be instantly observable as a regression.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourceFilePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var consumerFilePath = workspace.GetPath("SampleApp", "AnimalCoordinator.cs");
        File.WriteAllText(
            consumerFilePath,
            "using SampleLib;\n\npublic class AnimalCoordinator\n{\n    public AnimalCoordinator(AnimalService service)\n    {\n    }\n}\n");

        const string distinctiveSource = """
            using System.Threading;

            namespace SampleLib;

            public class AnimalService
            {


                public    void    MakeThemSpeak(   IEnumerable<IAnimal>     animals   )
                {
                    foreach (var animal in animals)
                    {
                        var sound = animal.Speak();
                        Console.WriteLine($"{animal.Name} says {sound}");
                    }
                }

                public int CountAnimals(List<IAnimal> animals)
                {
                    return animals.Count;
                }
            }

            """;
        File.WriteAllText(sourceFilePath, distinctiveSource);

        await workspace.LoadAsync(CancellationToken.None);

        var preview = await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            CancellationToken.None);

        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var sourceContents = await File.ReadAllTextAsync(sourceFilePath, CancellationToken.None);

        // (1) Distinctive parameter-list spacing survives the preview+apply round-trip.
        StringAssert.Contains(
            sourceContents,
            "MakeThemSpeak(   IEnumerable<IAnimal>     animals   )",
            $"Source parameter-list whitespace was collapsed (FORMAT-BUG-002 regression).\nFile contents:\n{sourceContents}");

        // (2) Distinctive spacing inside the signature (between modifier, return type, and name)
        //     survives. NormalizeWhitespace would rewrite `public    void    MakeThemSpeak` as
        //     `public void MakeThemSpeak`.
        StringAssert.Contains(
            sourceContents,
            "public    void    MakeThemSpeak",
            $"Source signature whitespace was collapsed (FORMAT-BUG-002 regression).\nFile contents:\n{sourceContents}");

        // (3) Multi-line blank-line separation between members survives. The original source was
        //     written with `\n` line endings; the AddBaseType edit may introduce a `\r\n` where
        //     the class body's `{` was relocated, but the two consecutive blank lines between `{`
        //     and the first member must survive intact. Normalize to `\n` before asserting.
        var normalized = sourceContents.Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(
            normalized,
            "\n{\n\n\n    public",
            $"Source blank-line structure was collapsed (FORMAT-BUG-002 regression).\nFile contents:\n{sourceContents}");

        // (4) The targeted `: IAnimalService` edit was applied with correct spacing.
        StringAssert.Contains(sourceContents, "class AnimalService : IAnimalService");
        Assert.IsFalse(
            sourceContents.Contains("IAnimalService{", StringComparison.Ordinal),
            $"Source class has `{{` glued onto the interface base type (regression).\nFile contents:\n{sourceContents}");

        // (5) Consumer parameter-type replacement preserved the space between type and name.
        var consumerContents = await File.ReadAllTextAsync(consumerFilePath, CancellationToken.None);
        StringAssert.Contains(
            consumerContents,
            "IAnimalService service",
            $"Consumer parameter lost whitespace between type and name (FORMAT-BUG-002 regression).\nFile contents:\n{consumerContents}");
        Assert.IsFalse(
            consumerContents.Contains("IAnimalServiceservice", StringComparison.Ordinal),
            $"Consumer parameter has glued-together type and name (FORMAT-BUG-002 regression).\nFile contents:\n{consumerContents}");
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
