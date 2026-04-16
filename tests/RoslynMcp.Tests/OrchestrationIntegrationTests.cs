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

        var preview = await PackageMigrationOrchestrator.PreviewMigratePackageAsync(
            workspace.WorkspaceId,
            "Legacy.Package",
            "Modern.Package",
            "2.5.0",
            CancellationToken.None);

        var applyResult = await CompositeApplyOrchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
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
    public async Task FORMAT_BUG_003_Migrate_Package_Preview_Emits_MultiLine_ItemGroup_With_Matching_Indent()
    {
        // FORMAT-BUG-003 regression (dr-9-4-format-bug-003-produces-inline-itemgroup-xml):
        // Previously `migrate_package_preview` serialized via `document.ToString(SaveOptions.DisableFormatting)`
        // which (i) inlined the new `<ItemGroup><PackageReference /></ItemGroup>` onto a single line,
        // (ii) glued `</Project>` onto the preceding element, and (iii) left an empty prior ItemGroup.
        // The fix emits via an indenting XmlWriter and inserts whitespace trivia around the new
        // ItemGroup, so each PackageReference must be on its own indented line.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sampleLibProject = workspace.GetPath("SampleLib", "SampleLib.csproj");
        InjectPackageReference(sampleLibProject, "Legacy.Package");
        InjectCentralPackageVersion(workspace.GetPath("Directory.Packages.props"), "Legacy.Package", "1.0.0");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await PackageMigrationOrchestrator.PreviewMigratePackageAsync(
            workspace.WorkspaceId,
            "Legacy.Package",
            "Modern.Package",
            "2.5.0",
            CancellationToken.None);

        // Preview diff must not contain inline ItemGroups or glued closing Project tags in any
        // ADDED lines (the pre-mutation `-` lines in the diff can contain the inline shape
        // because the fixture's XDocument.Save side-effected the csproj into a condensed form).
        var projectDiff = preview.Changes.First(change =>
            string.Equals(change.FilePath, sampleLibProject, StringComparison.OrdinalIgnoreCase)).UnifiedDiff;
        var addedLines = projectDiff.Split('\n')
            .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            .ToArray();
        Assert.IsFalse(addedLines.Any(line => line.Contains("<ItemGroup><PackageReference", StringComparison.Ordinal)),
            $"FORMAT-BUG-003 regression: ItemGroup must not inline the PackageReference in added lines. Diff:\n{projectDiff}");
        Assert.IsFalse(addedLines.Any(line => line.Contains("</ItemGroup></Project>", StringComparison.Ordinal)),
            $"FORMAT-BUG-003 regression: ItemGroup close must not be glued to Project close in added lines. Diff:\n{projectDiff}");
        Assert.IsFalse(addedLines.Any(line => line.Contains("<ItemGroup />", StringComparison.Ordinal)
            || line.Contains("<ItemGroup></ItemGroup>", StringComparison.Ordinal)),
            $"FORMAT-BUG-003 regression: no orphan empty ItemGroup should remain in added lines. Diff:\n{projectDiff}");

        // Apply and re-inspect the actual csproj contents for layout.
        var applyResult = await CompositeApplyOrchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var csprojText = await File.ReadAllTextAsync(sampleLibProject, CancellationToken.None);
        Assert.IsFalse(csprojText.Contains("<ItemGroup><PackageReference", StringComparison.Ordinal),
            $"FORMAT-BUG-003 regression on disk: ItemGroup must not inline the PackageReference. File:\n{csprojText}");
        Assert.IsFalse(csprojText.Contains("</ItemGroup></Project>", StringComparison.Ordinal),
            $"FORMAT-BUG-003 regression on disk: ItemGroup close must not be glued to Project close. File:\n{csprojText}");

        // Each PackageReference must own its own line with two-space parent indentation inherited
        // from the existing PropertyGroup layout.
        var packageReferenceLine = csprojText.Split('\n').Select(line => line.TrimEnd('\r'))
            .SingleOrDefault(line => line.Contains("PackageReference Include=\"Modern.Package\"", StringComparison.Ordinal))
            ?? throw new AssertFailedException("Modern.Package PackageReference line not found.");
        StringAssert.StartsWith(packageReferenceLine, "    <PackageReference",
            "FORMAT-BUG-003 regression: PackageReference must be indented with four spaces inside an ItemGroup.");
    }

    [TestMethod]
    public async Task FORMAT_BUG_003_Migrate_Package_Preview_Preserves_Utf8_Declaration_When_Present()
    {
        // FORMAT-BUG-003 regression (encoding half): the pre-fix serializer wrote via a
        // StringBuilder-backed XmlWriter, which emitted `<?xml version="1.0" encoding="utf-16"?>`
        // because StringBuilder is UTF-16 internally. Persisting that text as UTF-8 to disk
        // produced files whose declaration lied about their encoding, and subsequent readers
        // threw "There is no Unicode byte order mark. Cannot switch to Unicode." The fix writes
        // to a MemoryStream backed by UTF-8, so any emitted declaration correctly advertises utf-8.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sampleLibProject = workspace.GetPath("SampleLib", "SampleLib.csproj");
        InjectPackageReference(sampleLibProject, "Legacy.Package");
        InjectCentralPackageVersion(workspace.GetPath("Directory.Packages.props"), "Legacy.Package", "1.0.0");
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await PackageMigrationOrchestrator.PreviewMigratePackageAsync(
            workspace.WorkspaceId,
            "Legacy.Package",
            "Modern.Package",
            "2.5.0",
            CancellationToken.None);

        var applyResult = await CompositeApplyOrchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var csprojText = await File.ReadAllTextAsync(sampleLibProject, CancellationToken.None);
        Assert.IsFalse(csprojText.Contains("encoding=\"utf-16\"", StringComparison.OrdinalIgnoreCase),
            $"FORMAT-BUG-003 regression: csproj must not advertise utf-16 encoding when persisted as UTF-8. File:\n{csprojText}");

        // XDocument.Load must succeed — the pre-fix output threw "There is no Unicode byte order
        // mark" here. Use the default Load path (reads as UTF-8) to mirror real consumers.
        var reloaded = XDocument.Load(sampleLibProject);
        Assert.IsNotNull(reloaded.Root);
    }

    [TestMethod]
    public async Task Split_Class_Preview_And_Apply_Creates_New_Partial_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var newFilePath = workspace.GetPath("SampleLib", "Dog.Behavior.cs");

        var preview = await ClassSplitOrchestrator.PreviewSplitClassAsync(
            workspace.WorkspaceId,
            dogFilePath,
            "Dog",
            ["Fetch"],
            "Dog.Behavior.cs",
            CancellationToken.None);

        var applyResult = await CompositeApplyOrchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(newFilePath));

        var originalContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var newContents = await File.ReadAllTextAsync(newFilePath, CancellationToken.None);

        Assert.IsFalse(originalContents.Contains("void Fetch", StringComparison.Ordinal));
        Assert.IsTrue(newContents.Contains("void Fetch", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FORMAT_BUG_006_Split_Class_Preview_Does_Not_Duplicate_Leading_Trivia_Onto_Second_Partial()
    {
        // FORMAT-BUG-006 regression (dr-9-11-format-bug-006-duplicates-leading-trivia-into-b):
        // Pre-fix, `split_class_preview` carried the original type declaration's leading trivia
        // (XML doc comment, license header, explanatory single-line comment) AND its attribute
        // lists onto BOTH the original partial AND the newly created partial. Post-fix, the
        // first partial keeps those decorations and the second partial gets only minimal
        // leading trivia (a leading newline) and no attribute lists.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var newFilePath = workspace.GetPath("SampleLib", "Dog.Behavior.cs");

        // Overwrite the canonical Dog.cs with a version carrying a license header + XML doc
        // + attribute + explanatory single-line comment. Each artefact is unique so we can
        // grep for it independently.
        const string DogSource = "// SPDX-License-Identifier: MIT (LICENSE-HEADER-FORMAT-BUG-006)\n"
            + "using System;\n"
            + "\n"
            + "namespace SampleLib;\n"
            + "\n"
            + "/// <summary>XMLDOC-FORMAT-BUG-006: A canine.</summary>\n"
            + "// EXPLANATORY-FORMAT-BUG-006: extra context for maintainers.\n"
            + "[Serializable]\n"
            + "public class Dog : IAnimal\n"
            + "{\n"
            + "    public string Name => \"Dog\";\n"
            + "\n"
            + "    public string Speak() => \"Woof\";\n"
            + "\n"
            + "    public void Fetch(string item)\n"
            + "    {\n"
            + "        Console.WriteLine($\"Fetching {item}\");\n"
            + "    }\n"
            + "}\n";
        await File.WriteAllTextAsync(dogFilePath, DogSource, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var preview = await ClassSplitOrchestrator.PreviewSplitClassAsync(
            workspace.WorkspaceId,
            dogFilePath,
            "Dog",
            ["Fetch"],
            "Dog.Behavior.cs",
            CancellationToken.None);

        var applyResult = await CompositeApplyOrchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);
        Assert.IsTrue(File.Exists(newFilePath), "split_class_preview apply must have created the new partial file.");

        var originalContents = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var newContents = await File.ReadAllTextAsync(newFilePath, CancellationToken.None);

        // The first partial (original file) keeps every decoration.
        StringAssert.Contains(originalContents, "LICENSE-HEADER-FORMAT-BUG-006",
            "FORMAT-BUG-006 regression: license header must remain on the first partial.");
        StringAssert.Contains(originalContents, "XMLDOC-FORMAT-BUG-006",
            "FORMAT-BUG-006 regression: XML doc must remain on the first partial.");
        StringAssert.Contains(originalContents, "EXPLANATORY-FORMAT-BUG-006",
            "FORMAT-BUG-006 regression: explanatory comment must remain on the first partial.");
        StringAssert.Contains(originalContents, "[Serializable]",
            "FORMAT-BUG-006 regression: attribute must remain on the first partial.");

        // The second partial (new file) must NOT carry any of those decorations.
        Assert.IsFalse(newContents.Contains("LICENSE-HEADER-FORMAT-BUG-006", StringComparison.Ordinal),
            $"FORMAT-BUG-006 regression: license header leaked into the new partial. New file:\n{newContents}");
        Assert.IsFalse(newContents.Contains("XMLDOC-FORMAT-BUG-006", StringComparison.Ordinal),
            $"FORMAT-BUG-006 regression: XML doc comment duplicated onto the new partial. New file:\n{newContents}");
        Assert.IsFalse(newContents.Contains("EXPLANATORY-FORMAT-BUG-006", StringComparison.Ordinal),
            $"FORMAT-BUG-006 regression: explanatory comment duplicated onto the new partial. New file:\n{newContents}");
        Assert.IsFalse(newContents.Contains("[Serializable]", StringComparison.Ordinal),
            $"FORMAT-BUG-006 regression: attribute duplicated onto the new partial. New file:\n{newContents}");

        // Sanity: the new partial must still hold the moved member and declare the partial type.
        StringAssert.Contains(newContents, "void Fetch",
            "split_class_preview must move the selected member into the new partial.");
        StringAssert.Contains(newContents, "partial class Dog",
            "split_class_preview must mark the new type as partial.");
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

        var preview = await ExtractAndWireOrchestrator.PreviewExtractAndWireInterfaceAsync(
            workspace.WorkspaceId,
            sourceFilePath,
            "AnimalService",
            "IAnimalService",
            "Contracts",
            updateDiRegistrations: true,
            CancellationToken.None);

        var applyResult = await CompositeApplyOrchestrator.ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None);
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
