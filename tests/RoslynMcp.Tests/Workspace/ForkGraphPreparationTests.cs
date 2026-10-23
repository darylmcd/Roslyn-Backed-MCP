using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkGraphPreparationTests
{
    [TestMethod]
    public void Snapshot_AllowsDocumentAndProjectReferenceChanges()
    {
        using var fixture = new Fixture();
        var modified = fixture.Solution.AddProjectReference(fixture.First, new ProjectReference(fixture.Second));
        modified = modified.AddDocument(DocumentId.CreateNewId(fixture.First), "Added.cs", SourceText.From("class Added {}"),
            filePath: Path.Combine(fixture.Root, "Added.cs"));
        ForkGraphPreparation.ValidateSnapshot(fixture.Solution, modified, fixture.Root, CancellationToken.None);
    }

    [TestMethod]
    public async Task Preparation_ObservesPreCanceledToken()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => ForkGraphPreparation.ValidateSnapshotShape(
            fixture.Solution, fixture.Solution, fixture.Root, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => ForkGraphPreparation.ValidateSnapshot(
            fixture.Solution, fixture.Solution, fixture.Root, cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ForkGraphPreparation.VerifyAsync(
            fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, cancellation.Token));
    }

    [TestMethod]
    public void SnapshotShape_DoesNotBindHistoricalPreviewToCurrentDiskBeforeRebase()
    {
        using var fixture = new Fixture();
        var modified = fixture.Solution.AddDocument(DocumentId.CreateNewId(fixture.First), "Added.cs",
            SourceText.From("class Added {}"), filePath: Path.Combine(fixture.Root, "Added.cs"));
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "Second.csproj"))));
        File.Copy(Path.Combine(fixture.Fork, "First.csproj"), Path.Combine(fixture.Root, "First.csproj"), true);
        ForkGraphPreparation.ValidateSnapshotShape(fixture.Solution, modified, fixture.Root, CancellationToken.None);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ForkGraphPreparation.ValidateSnapshot(fixture.Solution, modified, fixture.Root, CancellationToken.None));
    }

    [TestMethod]
    public void Snapshot_RejectsOptionsAndAdditionalDocumentChanges()
    {
        using var fixture = new Fixture();
        var changedOptions = fixture.Solution.WithProjectParseOptions(fixture.First,
            new CSharpParseOptions(LanguageVersion.CSharp10));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ForkGraphPreparation.ValidateSnapshot(fixture.Solution, changedOptions, fixture.Root, CancellationToken.None));
        var addedDocument = fixture.Solution.AddAdditionalDocument(DocumentId.CreateNewId(fixture.First),
            "settings.json", SourceText.From("{}"), filePath: Path.Combine(fixture.Root, "settings.json"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ForkGraphPreparation.ValidateSnapshot(fixture.Solution, addedDocument, fixture.Root, CancellationToken.None));
        var addedReference = fixture.Solution.AddMetadataReference(fixture.First,
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ForkGraphPreparation.ValidateSnapshot(fixture.Solution, addedReference, fixture.Root, CancellationToken.None));
        var addedConfig = fixture.Solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(fixture.First),
            ".editorconfig", SourceText.From("root = true"), filePath: Path.Combine(fixture.Root, ".editorconfig"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ForkGraphPreparation.ValidateSnapshot(fixture.Solution, addedConfig, fixture.Root, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_FindsConditionalInnerFrameworkEdges()
    {
        using var fixture = new Fixture();
        fixture.WriteProject("First", new XElement("PropertyGroup", new XElement("TargetFrameworks", "net8.0;net10.0")),
            new XElement("ItemGroup", new XAttribute("Condition", "'$(TargetFramework)' == 'net10.0'"),
                new XElement("ProjectReference", new XAttribute("Include", "Second.csproj"))));
        File.Copy(Path.Combine(fixture.Fork, "First.csproj"), Path.Combine(fixture.Root, "First.csproj"), true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
        var expected = fixture.Solution.AddProjectReference(fixture.First, new ProjectReference(fixture.Second));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, expected, fixture.Root, fixture.Fork, CancellationToken.None));
        // Roslyn represents each framework as a project with the same physical path.
        // The net8.0 graph intentionally has no edge; both profiles must be present.
        expected = expected.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "First-net8.0", "First", LanguageNames.CSharp, filePath: Path.Combine(fixture.Root, "First.csproj")));
        await ForkGraphPreparation.VerifyAsync(expected, expected, fixture.Root, fixture.Fork, CancellationToken.None);
        // Swapping the physical contexts preserves the union, but must still fail.
        fixture.WriteProject("First", new XElement("PropertyGroup", new XElement("TargetFrameworks", "net8.0;net10.0")),
            new XElement("ItemGroup", new XAttribute("Condition", "'$(TargetFramework)' == 'net8.0'"),
                new XElement("ProjectReference", new XAttribute("Include", "Second.csproj"))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(expected, expected, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_RequiresAliasesAndEmbedInteropTypesToMatch()
    {
        using var fixture = new Fixture();
        var expected = fixture.Solution.AddProjectReference(fixture.First,
            new ProjectReference(fixture.Second, aliases: ["Special"], embedInteropTypes: true));
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "Second.csproj"))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, expected, fixture.Root, fixture.Fork, CancellationToken.None));
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "Second.csproj"), new XElement("Aliases", "Special"),
            new XElement("EmbedInteropTypes", "true"))));
        await ForkGraphPreparation.VerifyAsync(fixture.Solution, expected, fixture.Root, fixture.Fork, CancellationToken.None);
    }

    [TestMethod]
    public async Task Verify_RejectsMissingExpectedEdge()
    {
        using var fixture = new Fixture();
        var expected = fixture.Solution.AddProjectReference(fixture.First, new ProjectReference(fixture.Second));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, expected, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("OutputPath")]
    [DataRow("MSBuildProjectExtensionsPath")]
    [DataRow("RestoreOutputPath")]
    public async Task Verify_RejectsOutputsIntoOriginalSource(string property)
    {
        using var fixture = new Fixture();
        fixture.WriteProject("First", new XElement("PropertyGroup",
            new XElement(property, Path.Combine(fixture.Root, "original-output"))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_RejectsImportedSourceFileEvenWhenAllProjectsPropertyOmitsIt()
    {
        using var fixture = new Fixture();
        var import = Path.Combine(fixture.Root, "Source.props");
        File.WriteAllText(import, "<Project><PropertyGroup><MSBuildAllProjects /></PropertyGroup></Project>");
        fixture.WriteProject("First", new XElement("Import", new XAttribute("Project", import)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_RejectsSourceInputAndAllowsImportsOutsideSource()
    {
        using var fixture = new Fixture();
        var externalImport = Path.Combine(fixture.External, "Sdk.props");
        File.WriteAllText(externalImport, "<Project />");
        fixture.WriteProject("First", new XElement("Import", new XAttribute("Project", externalImport)));
        await ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None);
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("Compile",
            new XAttribute("Include", Path.Combine(fixture.Root, "Original.cs")))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_PreservesConfiguredGlobalProperties()
    {
        using var fixture = new Fixture();
        MsBuildInitializer.EnsureInitialized();
        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string> { ["Configuration"] = "Release" });
        var expected = workspace.CurrentSolution.AddProject(ProjectInfo.Create(fixture.First,
            VersionStamp.Create(), "First", "First", LanguageNames.CSharp,
            filePath: Path.Combine(fixture.Root, "First.csproj")));
        fixture.WriteProject("First", new XElement("PropertyGroup",
            new XAttribute("Condition", "'$(Configuration)' == 'Release'"),
            new XElement("OutputPath", Path.Combine(fixture.Root, "original-output"))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(expected, expected, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_RejectsInnerFrameworkSourceImport()
    {
        using var fixture = new Fixture();
        var import = Path.Combine(fixture.Root, "Inner.props");
        File.WriteAllText(import, "<Project />");
        fixture.WriteProject("First", new XElement("PropertyGroup", new XElement("TargetFrameworks", "net8.0;net10.0")),
            new XElement("Import", new XAttribute("Condition", "'$(TargetFramework)' == 'net10.0'"),
                new XAttribute("Project", import)));
        File.Copy(Path.Combine(fixture.Fork, "First.csproj"), Path.Combine(fixture.Root, "First.csproj"), true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_RequiresOrdinaryDocumentAdditionsAndRemovalsInCompileInventory()
    {
        using var fixture = new Fixture();
        var document = DocumentId.CreateNewId(fixture.First);
        var modified = fixture.Solution.AddDocument(document, "Added.cs", SourceText.From("class Added {}"),
            filePath: Path.Combine(fixture.Root, "Added.cs"));
        ForkGraphPreparation.ValidateSnapshot(fixture.Solution, modified, fixture.Root, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            fixture.Solution, modified, fixture.Root, fixture.Fork, CancellationToken.None));
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Added.cs"))));
        await ForkGraphPreparation.VerifyAsync(fixture.Solution, modified, fixture.Root, fixture.Fork, CancellationToken.None);
        File.Copy(Path.Combine(fixture.Fork, "First.csproj"), Path.Combine(fixture.Root, "First.csproj"), true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            modified, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
        fixture.WriteProject("First");
        await ForkGraphPreparation.VerifyAsync(modified, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None);
    }

    [TestMethod]
    public async Task Snapshot_AllowsUnchangedExternalDocumentsButRejectsEditingThem()
    {
        using var fixture = new Fixture();
        var document = DocumentId.CreateNewId(fixture.First);
        var path = Path.Combine(fixture.External, "Package.cs");
        var original = fixture.Solution.AddDocument(document, "Package.cs", SourceText.From("class Package {}"), filePath: path);
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", path))));
        File.Copy(Path.Combine(fixture.Fork, "First.csproj"), Path.Combine(fixture.Root, "First.csproj"), true);
        ForkGraphPreparation.ValidateSnapshot(original, original, fixture.Root, CancellationToken.None);
        await ForkGraphPreparation.VerifyAsync(original, original, fixture.Root, fixture.Fork, CancellationToken.None);
        var modified = original.WithDocumentText(document, SourceText.From("class Changed {}"));
        Assert.ThrowsExactly<InvalidOperationException>(() => ForkGraphPreparation.ValidateSnapshot(original, modified, fixture.Root, CancellationToken.None));
    }

    [TestMethod]
    public void Snapshot_RejectsAmbiguousFrameworkChangesBeforeCopy()
    {
        using var fixture = new Fixture();
        var duplicate = ProjectId.CreateNewId();
        var original = fixture.Solution.AddProject(ProjectInfo.Create(duplicate, VersionStamp.Create(),
            "First-other-framework", "First", LanguageNames.CSharp, filePath: Path.Combine(fixture.Root, "First.csproj")));
        var modified = original.AddProjectReference(fixture.First, new ProjectReference(fixture.Second));
        Assert.ThrowsExactly<InvalidOperationException>(() => ForkGraphPreparation.ValidateSnapshot(original, modified, fixture.Root, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_PreservesNonCompilationProjectReferences()
    {
        using var fixture = new Fixture();
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "Second.csproj"), new XElement("ReferenceOutputAssembly", "false"),
            new XElement("OutputItemType", "Analyzer"))));
        File.Copy(Path.Combine(fixture.Fork, "First.csproj"), Path.Combine(fixture.Root, "First.csproj"), true);
        ForkGraphPreparation.ValidateSnapshot(fixture.Solution, fixture.Solution, fixture.Root, CancellationToken.None);
        await ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None);
        fixture.WriteProject("First");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_DistinguishesSourceDirectoryMetadataFromFileInputs()
    {
        using var fixture = new Fixture();
        fixture.WriteProject("First", new XElement("ItemGroup",
            new XElement("SourceRoot", new XAttribute("Include", fixture.Root)),
            new XElement("Folder", new XAttribute("Include", fixture.Root))));
        await ForkGraphPreparation.VerifyAsync(fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None);
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("Compile",
            new XAttribute("Include", Path.Combine(fixture.Root, "NotCreatedYet.cs")))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
        var customInput = Path.Combine(fixture.Root, "Custom.input");
        File.WriteAllText(customInput, "input");
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("CustomFileInput",
            new XAttribute("Include", customInput))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_SdkMissingGlobalConfigProbesAreNotCompilerInputs()
    {
        using var fixture = new Fixture();
        var original = fixture.Solution.AddDocument(DocumentId.CreateNewId(fixture.First), "Input.cs",
            SourceText.From("class Input {}"), filePath: Path.Combine(fixture.Root, "Input.cs"));
        File.WriteAllText(Path.Combine(fixture.Root, "Input.cs"), "class Input {}");
        File.WriteAllText(Path.Combine(fixture.Fork, "Input.cs"), "class Input {}");
        var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                new XElement("EnableDefaultCompileItems", "false")),
            new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Input.cs")))));
        project.Save(Path.Combine(fixture.Root, "First.csproj"));
        project.Save(Path.Combine(fixture.Fork, "First.csproj"));
        await ForkGraphPreparation.VerifyAsync(original, original, fixture.Root, fixture.Fork, CancellationToken.None);
        File.WriteAllText(Path.Combine(fixture.Root, ".globalconfig"), "is_global = true");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            original, original, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    [TestMethod]
    public async Task Verify_ExplicitMissingSourceGlobalConfigRemainsAnInput()
    {
        using var fixture = new Fixture();
        fixture.WriteProject("First", new XElement("ItemGroup", new XElement("GlobalAnalyzerConfigFiles",
            new XAttribute("Include", Path.Combine(fixture.Root, "Missing.globalconfig")))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ForkGraphPreparation.VerifyAsync(
            fixture.Solution, fixture.Solution, fixture.Root, fixture.Fork, CancellationToken.None));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();
        private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "ForkGraphPreparation-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public string Fork { get; }
        public string External { get; }
        public ProjectId First { get; } = ProjectId.CreateNewId();
        public ProjectId Second { get; } = ProjectId.CreateNewId();
        public Solution Solution { get; }

        public Fixture()
        {
            Root = Path.Combine(_temporaryRoot, "source");
            Fork = Path.Combine(Root, ".roslynmcp", "forks", "copy");
            External = Path.Combine(_temporaryRoot, "sdk");
            Directory.CreateDirectory(Fork);
            Directory.CreateDirectory(External);
            Solution = _workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(First, VersionStamp.Create(), "First", "First", LanguageNames.CSharp,
                    filePath: Path.Combine(Root, "First.csproj")))
                .AddProject(ProjectInfo.Create(Second, VersionStamp.Create(), "Second", "Second", LanguageNames.CSharp,
                    filePath: Path.Combine(Root, "Second.csproj")));
            WriteProject("First");
            WriteProject("Second");
            File.Copy(Path.Combine(Fork, "First.csproj"), Path.Combine(Root, "First.csproj"));
            File.Copy(Path.Combine(Fork, "Second.csproj"), Path.Combine(Root, "Second.csproj"));
        }

        public void WriteProject(string name, params XElement[] elements) =>
            new XDocument(new XElement("Project", elements)).Save(Path.Combine(Fork, name + ".csproj"));

        public void Dispose()
        {
            _workspace.Dispose();
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
