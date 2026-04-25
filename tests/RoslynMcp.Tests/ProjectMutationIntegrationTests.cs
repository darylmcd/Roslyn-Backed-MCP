using System.Xml.Linq;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ProjectMutationIntegrationTests : IsolatedWorkspaceTestBase
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
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");

        var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspace.WorkspaceId,
            new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var projectXml = XDocument.Load(projectFilePath);
        Assert.IsTrue(projectXml.Descendants("PackageReference").Any(element =>
            string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Add_Package_Reference_Preview_Preserves_ItemGroup_Indentation()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var originalProjectContent = await File.ReadAllTextAsync(projectFilePath, CancellationToken.None);
        var customizedProjectContent = originalProjectContent.Replace(
            "</Project>",
            "  <ItemGroup>\n    <PackageReference Include=\"Existing.Package\" Version=\"1.0.0\" />\n  </ItemGroup>\n</Project>",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectFilePath, customizedProjectContent, CancellationToken.None);

        var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspace.WorkspaceId,
            new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var projectContents = await File.ReadAllTextAsync(projectFilePath, CancellationToken.None);
        StringAssert.Contains(projectContents, "\n    <PackageReference Include=\"Humanizer.Core\"");
    }

    [TestMethod]
    public async Task Add_Project_Reference_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleApp", "SampleApp.csproj");

        var preview = await ProjectMutationService.PreviewAddProjectReferenceAsync(
            workspace.WorkspaceId,
            new AddProjectReferenceDto("SampleApp", "SampleLib.Tests"),
            CancellationToken.None);

        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.FilePath, projectFilePath, StringComparison.OrdinalIgnoreCase)));

        var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var projectXml = XDocument.Load(projectFilePath);
        Assert.IsTrue(projectXml.Descendants("ProjectReference").Any(element =>
            string.Equals(Path.GetFileName((string?)element.Attribute("Include")), "SampleLib.Tests.csproj", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Set_Project_Property_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");

        var preview = await ProjectMutationService.PreviewSetProjectPropertyAsync(
            workspace.WorkspaceId,
            new SetProjectPropertyDto("SampleLib", "LangVersion", "preview"),
            CancellationToken.None);

        var applyResult = await ProjectMutationService.ApplyProjectMutationAsync(preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        var projectXml = XDocument.Load(projectFilePath);
        Assert.AreEqual("preview", projectXml.Descendants("LangVersion").First().Value);
    }

    [TestMethod]
    public async Task FLAG_13B_Set_Project_Property_Preview_Emits_MultiLine_PropertyGroup()
    {
        // FLAG-13B regression: previously the diff added "<PropertyGroup><LangVersion>latest</LangVersion></PropertyGroup>"
        // on a single line. The fix uses XmlWriter with indentation, so the diff must show the
        // PropertyGroup elements on separate lines.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await ProjectMutationService.PreviewSetProjectPropertyAsync(
            workspace.WorkspaceId,
            new SetProjectPropertyDto("SampleLib", "LangVersion", "preview"),
            CancellationToken.None);

        Assert.IsNotNull(preview);
        var diff = preview.Changes.First().UnifiedDiff;
        Assert.IsFalse(diff.Contains("<PropertyGroup><LangVersion>", StringComparison.Ordinal),
            $"FLAG-13B regression: PropertyGroup must not be single-line. Diff:\n{diff}");
    }

    [TestMethod]
    public async Task FLAG_13A_Add_Package_Reference_Preview_Emits_MultiLine_ItemGroup()
    {
        // FLAG-13A regression: previously the diff added "</ItemGroup><ItemGroup>" on a single
        // line. The fix uses XmlWriter with indentation, so the diff must keep ItemGroup tags
        // on separate lines.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspace.WorkspaceId,
            new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        Assert.IsNotNull(preview);
        var diff = preview.Changes.First().UnifiedDiff;
        Assert.IsFalse(diff.Contains("</ItemGroup><ItemGroup>", StringComparison.Ordinal),
            $"FLAG-13A regression: ItemGroup tags must not be concatenated. Diff:\n{diff}");
    }

    [TestMethod]
    public async Task project_mutation_preview_xml_formatting_AddPackageReference_KeepsPropertyGroupAndItemGroupOnSeparateLines()
    {
        // project-mutation-preview-xml-formatting regression: the previous serializer left
        // </PropertyGroup> and the freshly-inserted <ItemGroup> on the same line because
        // ProjectMutationService.GetOrCreateItemGroup used two sequential AddAfterSelf calls
        // (LIFO ordering) instead of the FORMAT-BUG-003-fixed splice already present in
        // OrchestrationMsBuildXml. After consolidating onto OrchestrationMsBuildXml.GetOrCreateItemGroup,
        // the diff must show </PropertyGroup> and <ItemGroup> on separate lines AND the new
        // <PackageReference> indented as a child of <ItemGroup>, not glued to its opening tag.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspace.WorkspaceId,
            new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        Assert.IsNotNull(preview);
        var diff = preview.Changes.First().UnifiedDiff;
        Assert.IsFalse(diff.Contains("</PropertyGroup><ItemGroup>", StringComparison.Ordinal),
            $"project-mutation-preview-xml-formatting: </PropertyGroup> and <ItemGroup> must not be concatenated. Diff:\n{diff}");
        Assert.IsFalse(diff.Contains("<ItemGroup><PackageReference", StringComparison.Ordinal),
            $"project-mutation-preview-xml-formatting: <ItemGroup> and <PackageReference> must not be concatenated. Diff:\n{diff}");
        // Sanity: the diff should contain a properly-indented PackageReference line beginning
        // with the customary 4-space child indent inside <ItemGroup>.
        StringAssert.Contains(diff, "    <PackageReference Include=\"Humanizer.Core\"",
            $"Expected 4-space indented PackageReference. Diff:\n{diff}");
    }

    [TestMethod]
    public async Task project_mutation_preview_xml_formatting_SetProjectProperty_KeepsNewPropertyOnOwnLine()
    {
        // project-mutation-preview-xml-formatting regression: SetElementValue inserts the new
        // child XElement with no surrounding whitespace text nodes, which makes XmlWriter emit
        // `<TargetFramework>...</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup>`
        // on a single line whenever the property is being added (vs updated). The fix routes
        // through AddChildElementPreservingIndentation so the new property is on its own line
        // with proper child indent.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await ProjectMutationService.PreviewSetProjectPropertyAsync(
            workspace.WorkspaceId,
            new SetProjectPropertyDto("SampleLib", "LangVersion", "preview"),
            CancellationToken.None);

        Assert.IsNotNull(preview);
        var diff = preview.Changes.First().UnifiedDiff;
        // The new <LangVersion> sibling must NOT be glued to a preceding closing tag (e.g.
        // </ImplicitUsings><LangVersion> or </TreatWarningsAsErrors><LangVersion>).
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(diff, @"</[A-Za-z]+><LangVersion>"),
            $"project-mutation-preview-xml-formatting: <LangVersion> must not be glued to a preceding closing tag. Diff:\n{diff}");
        // It also must not be glued to </PropertyGroup> on the trailing side.
        Assert.IsFalse(diff.Contains("<LangVersion>preview</LangVersion></PropertyGroup>", StringComparison.Ordinal),
            $"project-mutation-preview-xml-formatting: <LangVersion> must not be on the same line as </PropertyGroup>. Diff:\n{diff}");
        // Sanity: the diff should contain a properly-indented <LangVersion> line.
        StringAssert.Contains(diff, "    <LangVersion>preview</LangVersion>",
            $"Expected 4-space indented <LangVersion>. Diff:\n{diff}");
    }

    [TestMethod]
    public async Task project_mutation_preview_xml_formatting_AddCentralPackageVersion_KeepsItemGroupOnSeparateLine()
    {
        // project-mutation-preview-xml-formatting regression: same root cause as the
        // package-reference and property-set defects — applied to Directory.Packages.props
        // mutations. Previously the new <ItemGroup> wrapping the <PackageVersion> landed on
        // the same line as </PropertyGroup> and the child was collapsed inside the wrapper.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var preview = await ProjectMutationService.PreviewAddCentralPackageVersionAsync(
            workspace.WorkspaceId,
            new AddCentralPackageVersionDto("Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        Assert.IsNotNull(preview);
        var diff = preview.Changes.First().UnifiedDiff;
        Assert.IsFalse(diff.Contains("</PropertyGroup><ItemGroup>", StringComparison.Ordinal),
            $"project-mutation-preview-xml-formatting: </PropertyGroup> and <ItemGroup> must not be concatenated. Diff:\n{diff}");
        Assert.IsFalse(diff.Contains("<ItemGroup><PackageVersion", StringComparison.Ordinal),
            $"project-mutation-preview-xml-formatting: <ItemGroup> and <PackageVersion> must not be concatenated. Diff:\n{diff}");
        // Sanity: the diff should contain a properly-indented PackageVersion line.
        StringAssert.Contains(diff, "    <PackageVersion Include=\"Humanizer.Core\"",
            $"Expected 4-space indented PackageVersion. Diff:\n{diff}");
    }

    [TestMethod]
    public async Task Add_And_Remove_Target_Framework_Preview_And_Apply_Updates_Isolated_Project_File()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");

        var addPreview = await ProjectMutationService.PreviewAddTargetFrameworkAsync(
            workspace.WorkspaceId,
            new AddTargetFrameworkDto("SampleLib", "net8.0"),
            CancellationToken.None);

        var addResult = await ProjectMutationService.ApplyProjectMutationAsync(addPreview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(addResult.Success, addResult.Error);

        var projectXml = XDocument.Load(projectFilePath);
        Assert.AreEqual("net10.0;net8.0", projectXml.Descendants("TargetFrameworks").First().Value);

        var removePreview = await ProjectMutationService.PreviewRemoveTargetFrameworkAsync(
            workspace.WorkspaceId,
            new RemoveTargetFrameworkDto("SampleLib", "net8.0"),
            CancellationToken.None);

        var removeResult = await ProjectMutationService.ApplyProjectMutationAsync(removePreview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(removeResult.Success, removeResult.Error);

        projectXml = XDocument.Load(projectFilePath);
        Assert.AreEqual("net10.0", projectXml.Descendants("TargetFramework").First().Value);
    }

    [TestMethod]
    public async Task Add_Target_Framework_When_Centralized_In_DirectoryBuildProps_Injects_Into_Csproj()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(
            Path.Combine(workspace.RootPath, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """,
            CancellationToken.None).ConfigureAwait(false);

        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        await File.WriteAllTextAsync(
            projectFilePath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """,
            CancellationToken.None).ConfigureAwait(false);

        await workspace.LoadAsync(CancellationToken.None).ConfigureAwait(false);

        var addPreview = await ProjectMutationService.PreviewAddTargetFrameworkAsync(
            workspace.WorkspaceId,
            new AddTargetFrameworkDto("SampleLib", "net8.0"),
            CancellationToken.None).ConfigureAwait(false);

        var addResult = await ProjectMutationService.ApplyProjectMutationAsync(addPreview.PreviewToken, CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(addResult.Success, addResult.Error);

        var projectXml = XDocument.Load(projectFilePath);
        var tfs = projectXml.Descendants("TargetFrameworks").First().Value;
        StringAssert.Contains(tfs, "net10.0");
        StringAssert.Contains(tfs, "net8.0");
    }

    [TestMethod]
    public async Task Set_Conditional_Property_Preview_And_Apply_Adds_Conditional_Property_Group()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        const string condition = "'$(Configuration)'=='Release'";

        var preview = await ProjectMutationService.PreviewSetConditionalPropertyAsync(
            workspace.WorkspaceId,
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

    [TestMethod]
    public async Task Add_And_Remove_Central_Package_Version_Preview_And_Apply_Updates_Directory_Packages_Props()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var packagesPropsPath = workspace.GetPath("Directory.Packages.props");

        var addPreview = await ProjectMutationService.PreviewAddCentralPackageVersionAsync(
            workspace.WorkspaceId,
            new AddCentralPackageVersionDto("Humanizer.Core", "2.14.1"),
            CancellationToken.None);

        var addResult = await ProjectMutationService.ApplyProjectMutationAsync(addPreview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(addResult.Success, addResult.Error);

        var propsXml = XDocument.Load(packagesPropsPath);
        Assert.IsTrue(propsXml.Descendants("PackageVersion").Any(element =>
            string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string?)element.Attribute("Version"), "2.14.1", StringComparison.OrdinalIgnoreCase)));

        var removePreview = await ProjectMutationService.PreviewRemoveCentralPackageVersionAsync(
            workspace.WorkspaceId,
            new RemoveCentralPackageVersionDto("Humanizer.Core"),
            CancellationToken.None);

        var removeResult = await ProjectMutationService.ApplyProjectMutationAsync(removePreview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(removeResult.Success, removeResult.Error);

        propsXml = XDocument.Load(packagesPropsPath);
        Assert.IsFalse(propsXml.Descendants("PackageVersion").Any(element =>
            string.Equals((string?)element.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Add_Package_Reference_Preview_When_Central_Package_Management_Is_Active_Omits_Version_And_Returns_Warning()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");

        var preview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspace.WorkspaceId,
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

    [TestMethod]
    public async Task Add_Then_Remove_PackageReference_LeavesNoOrphanBlankLines()
    {
        // apply-project-mutation-whitespace: pre-fix, removing a `<PackageReference>` left a
        // blank line where the element used to live AND an empty `<ItemGroup />` if that
        // element was the only child. Post-fix, neither artifact appears in the round-tripped
        // file. We don't require byte-identical round-trip because XmlWriter's `Indent = true`
        // serialization can normalize whitespace differently from the source — the bug-relevant
        // check is "no orphan blank lines / empty groups".
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var projectFilePath = workspace.GetPath("SampleLib", "SampleLib.csproj");

        var addPreview = await ProjectMutationService.PreviewAddPackageReferenceAsync(
            workspace.WorkspaceId,
            new AddPackageReferenceDto("SampleLib", "Humanizer.Core", "2.14.1"),
            CancellationToken.None);
        var addResult = await ProjectMutationService.ApplyProjectMutationAsync(
            addPreview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(addResult.Success, addResult.Error);

        var removePreview = await ProjectMutationService.PreviewRemovePackageReferenceAsync(
            workspace.WorkspaceId,
            new RemovePackageReferenceDto("SampleLib", "Humanizer.Core"),
            CancellationToken.None);
        var removeResult = await ProjectMutationService.ApplyProjectMutationAsync(
            removePreview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(removeResult.Success, removeResult.Error);

        var roundTrippedContent = await File.ReadAllTextAsync(projectFilePath, CancellationToken.None);
        var normalized = roundTrippedContent.Replace("\r\n", "\n");

        // The pre-fix bug: a stray `\n  \n` (blank line containing only indentation) between
        // `</PropertyGroup>` and `</Project>` after the round-trip.
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\n[ \t]+\n"),
            $"Round-tripped file must not contain a blank line with stray indentation. Got:\n{normalized}");

        // The pre-fix bug also left an empty `<ItemGroup />` after the last reference was removed.
        var roundtripDoc = XDocument.Parse(roundTrippedContent);
        Assert.IsFalse(roundtripDoc.Descendants("ItemGroup").Any(g => !g.Elements().Any()),
            "Round-tripped file must not contain empty <ItemGroup /> after the last reference is removed.");

        // Sanity: the PackageReference is gone.
        Assert.IsFalse(roundtripDoc.Descendants("PackageReference").Any(r =>
            string.Equals((string?)r.Attribute("Include"), "Humanizer.Core", StringComparison.OrdinalIgnoreCase)),
            "PackageReference must be absent after remove.");
    }

    [TestMethod]
    public async Task Add_Package_Reference_Preview_Rejects_Package_Already_Imported_From_Directory_Build_Props()
    {
        // add-package-reference-preview-cpm-duplicate-detection: when a package is already
        // present in the evaluated project graph because Directory.Build.props contributes a
        // `<PackageReference Include="..." />` for every project, trying to add the same
        // package via the tool must not silently emit a duplicate. The pre-fix behavior only
        // scanned the csproj XML, missed the imported reference, and happily added a second
        // `<PackageReference>` that NuGet later refuses.
        //
        // The sample fixture's Directory.Build.props already declares Microsoft.CodeAnalysis.NetAnalyzers
        // as a PackageReference for every project, so it acts as the "transitive-present" repro.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            ProjectMutationService.PreviewAddPackageReferenceAsync(
                workspace.WorkspaceId,
                new AddPackageReferenceDto("SampleLib", "Microsoft.CodeAnalysis.NetAnalyzers", "10.0.100"),
                CancellationToken.None));

        StringAssert.Contains(ex.Message, "already present");
        StringAssert.Contains(ex.Message, "Microsoft.CodeAnalysis.NetAnalyzers");
    }
}
