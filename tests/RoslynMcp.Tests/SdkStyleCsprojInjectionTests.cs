using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #5 — regression guard for `severity-medium-breaks-msbuild-until-csproj-is-hand`,
/// `dr-9-1-add-items-that-break-sdk-style-projects`, `dr-9-6-bug-compile-include-adds-explicit-on-sdk-auto-in`.
/// Two angles: (1) XML-shape detection of SDK-style + default compile items is correct across
/// the common csproj variants, (2) end-to-end create_file_apply on an SDK-style csproj does
/// not inject a duplicate <c>&lt;Compile&gt;</c> item.
/// </summary>
[TestClass]
public sealed class SdkStyleCsprojInjectionTests : TestBase
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
    public void IsSdkStyleWithDefaultCompileItems_AttributeForm_IsTrue()
    {
        var doc = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        Assert.IsTrue(ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(doc));
    }

    [TestMethod]
    public void IsSdkStyleWithDefaultCompileItems_ElementForm_IsTrue()
    {
        // Less common but valid (e.g., multi-SDK scenarios).
        var doc = XDocument.Parse("""
            <Project>
              <Sdk Name="Microsoft.NET.Sdk" />
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Assert.IsTrue(ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(doc));
    }

    [TestMethod]
    public void IsSdkStyleWithDefaultCompileItems_ExplicitlyDisabled_IsFalse()
    {
        var doc = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
            </Project>
            """);
        Assert.IsFalse(
            ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(doc),
            "Projects that opt out of default compile items WANT explicit injections; the guard must not activate.");
    }

    [TestMethod]
    public void IsSdkStyleWithDefaultCompileItems_LegacyNonSdk_IsFalse()
    {
        // Pre-SDK (Full Framework / tools-version) csproj. Must not trigger the guard —
        // legacy projects require explicit <Compile> items to build.
        var doc = XDocument.Parse("""
            <Project ToolsVersion="15.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Class1.cs" />
              </ItemGroup>
            </Project>
            """);
        Assert.IsFalse(ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(doc));
    }

    [TestMethod]
    public void IsSdkStyleWithDefaultCompileItems_NullOrMissing_IsFalse()
    {
        Assert.IsFalse(ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems((XDocument?)null));
        Assert.IsFalse(ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(
            Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.csproj"),
            NullLogger.Instance));
    }

    [TestMethod]
    public async Task CreateFile_On_SdkStyle_Csproj_Does_Not_Inject_Compile_Item()
    {
        // End-to-end: copy the SampleSolution into a throwaway directory, create a file via
        // the apply path, and assert the csproj bytes are byte-identical to the pre-apply
        // state. Before Item #5 this test would fail with the csproj containing a new
        // <Compile Include="IsolatedFile.cs" /> entry; after the fix the csproj is untouched.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var workspaceId = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None)
            .ContinueWith(t => t.Result.WorkspaceId);

        try
        {
            var sampleLibCsproj = Path.Combine(solutionDir, "SampleLib", "SampleLib.csproj");
            Assert.IsTrue(File.Exists(sampleLibCsproj), $"Fixture missing SampleLib.csproj at {sampleLibCsproj}");

            var preApplyCsproj = await File.ReadAllTextAsync(sampleLibCsproj);

            // Sanity: this test is only meaningful if the fixture project is actually SDK-style.
            Assert.IsTrue(
                ProjectMetadataParser.IsSdkStyleWithDefaultCompileItems(sampleLibCsproj, NullLogger.Instance),
                "Sample fixture SampleLib.csproj must be SDK-style for this test to cover the bug.");

            var newFilePath = Path.Combine(solutionDir, "SampleLib", "Item5Guard_Generated.cs");
            var sampleLibProject = WorkspaceManager.GetCurrentSolution(workspaceId).Projects
                .FirstOrDefault(p => string.Equals(p.Name, "SampleLib", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("SampleLib project missing from solution.");

            var previewDto = await FileOperationService.PreviewCreateFileAsync(
                workspaceId,
                new CreateFileDto(
                    ProjectName: sampleLibProject.Name,
                    FilePath: newFilePath,
                    Content: "namespace SampleLib;\n\npublic static class Item5Guard\n{\n    public const string Marker = \"item-5\";\n}\n"),
                CancellationToken.None);

            await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(File.Exists(newFilePath), "The generated file must be on disk after apply.");

            var postApplyCsproj = await File.ReadAllTextAsync(sampleLibCsproj);
            Assert.AreEqual(
                preApplyCsproj,
                postApplyCsproj,
                "SDK-style csproj must be byte-identical before and after a create_file_apply. " +
                "TryApplyChanges tried to inject an explicit <Compile> item; the Item #5 snapshot/restore " +
                "logic must have undone the injection.");

            // Also verify the apply didn't silently skip the in-memory add (agent depends
            // on the file being visible to follow-up compile_check calls).
            var solutionAfter = WorkspaceManager.GetCurrentSolution(workspaceId);
            var sampleLibAfter = solutionAfter.Projects.First(p => string.Equals(p.Name, "SampleLib", StringComparison.Ordinal));
            Assert.IsTrue(
                sampleLibAfter.Documents.Any(d => string.Equals(d.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)),
                "The in-memory workspace must still contain the added document after the csproj restore.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; the fixture copy lives under the test artifacts directory.
        }
    }
}
