using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>csproj-reserialization-msbuildworkspace</c> /
/// <c>create-file-apply-csproj-side-effect-all-projects</c> (P2). Two angles:
/// <list type="number">
///   <item><description><see cref="CsprojSemanticEquality"/> correctly classifies trivia-only
///   diffs (BOM, line endings, blank lines, whitespace) as equivalent and classifies semantic
///   diffs (new PackageReference, property edit, new ItemGroup child) as different.</description></item>
///   <item><description>End-to-end: <c>create_file_apply</c> for ONE file in ONE project of a
///   multi-project solution leaves every OTHER csproj byte-identical on disk, even when
///   MSBuildWorkspace's internal project-file writer would have reserialized them with
///   BOM / CRLF / blank-line drift.</description></item>
/// </list>
/// </summary>
[TestClass]
public sealed class CsprojReserializationTests : TestBase
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

    // === CsprojSemanticEquality unit tests ===

    [TestMethod]
    public void AreXmlEquivalent_ByteIdentical_IsTrue()
    {
        const string Content = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        Assert.IsTrue(CsprojSemanticEquality.AreXmlEquivalent(Content, Content));
    }

    [TestMethod]
    public void AreXmlEquivalent_BomVsNoBom_IsTrue()
    {
        const string WithoutBom = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";
        var withBom = "\uFEFF" + WithoutBom;
        Assert.IsTrue(
            CsprojSemanticEquality.AreXmlEquivalent(WithoutBom, withBom),
            "Adding a UTF-8 BOM is trivia — should compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_LfVsCrlfLineEndings_IsTrue()
    {
        const string Lf = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";
        var crlf = Lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.IsTrue(
            CsprojSemanticEquality.AreXmlEquivalent(Lf, crlf),
            "LF-vs-CRLF line endings are trivia — should compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_BlankLinesCollapsed_IsTrue()
    {
        const string WithBlankLine = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="..\Other\Other.csproj" />
              </ItemGroup>
            </Project>
            """;
        const string NoBlankLine = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Other\Other.csproj" />
              </ItemGroup>
            </Project>
            """;
        Assert.IsTrue(
            CsprojSemanticEquality.AreXmlEquivalent(WithBlankLine, NoBlankLine),
            "Blank lines between elements are trivia — should compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_TrailingNewlineStripped_IsTrue()
    {
        const string WithTrailing = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n";
        const string WithoutTrailing = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        Assert.IsTrue(
            CsprojSemanticEquality.AreXmlEquivalent(WithTrailing, WithoutTrailing),
            "Trailing newline is trivia — should compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_WhitespaceTrimmedLeafValue_IsTrue()
    {
        const string Padded = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>  net10.0  </TargetFramework></PropertyGroup></Project>";
        const string Tight = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        Assert.IsTrue(
            CsprojSemanticEquality.AreXmlEquivalent(Padded, Tight),
            "MSBuild trims leaf-element whitespace at evaluation — should compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_NewPackageReference_IsFalse()
    {
        const string Original = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        const string WithPackage = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.0" />
              </ItemGroup>
            </Project>
            """;
        Assert.IsFalse(
            CsprojSemanticEquality.AreXmlEquivalent(Original, WithPackage),
            "Adding a PackageReference is a semantic diff — should NOT compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_PropertyValueChanged_IsFalse()
    {
        const string Net10 = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """;
        const string Net8 = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """;
        Assert.IsFalse(
            CsprojSemanticEquality.AreXmlEquivalent(Net10, Net8),
            "Changing TargetFramework is a semantic diff — should NOT compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_AttributeValueChanged_IsFalse()
    {
        const string IncludeA = "<Project><ItemGroup><Compile Include=\"A.cs\" /></ItemGroup></Project>";
        const string IncludeB = "<Project><ItemGroup><Compile Include=\"B.cs\" /></ItemGroup></Project>";
        Assert.IsFalse(
            CsprojSemanticEquality.AreXmlEquivalent(IncludeA, IncludeB),
            "Changing an attribute value is a semantic diff — should NOT compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_ChildOrderChanged_IsFalse()
    {
        // Child-element order IS significant in MSBuild (PackageReference order affects transitive
        // resolution, Target order affects execution). The helper must treat reorder as a semantic diff.
        const string OrderA = """
            <Project>
              <ItemGroup>
                <PackageReference Include="A" />
                <PackageReference Include="B" />
              </ItemGroup>
            </Project>
            """;
        const string OrderB = """
            <Project>
              <ItemGroup>
                <PackageReference Include="B" />
                <PackageReference Include="A" />
              </ItemGroup>
            </Project>
            """;
        Assert.IsFalse(
            CsprojSemanticEquality.AreXmlEquivalent(OrderA, OrderB),
            "Child-element reorder is a semantic diff in MSBuild — should NOT compare equal.");
    }

    [TestMethod]
    public void AreXmlEquivalent_MalformedXml_ReturnsFalse()
    {
        const string Good = "<Project><PropertyGroup><X>1</X></PropertyGroup></Project>";
        const string Malformed = "<Project><PropertyGroup><X>1</X></PropertyGroup";
        Assert.IsFalse(
            CsprojSemanticEquality.AreXmlEquivalent(Good, Malformed),
            "Parse failure must return false — safer than silently treating as equivalent.");
    }

    // === End-to-end regression — multi-project drift reproduction ===

    [TestMethod]
    public async Task CreateFile_On_One_Project_Leaves_Other_Csprojs_Byte_Identical()
    {
        // Multi-project SampleSolution has 4 csprojs (SampleApp, SampleLib, SampleLib.Generators,
        // SampleLib.Tests). Before the fix, TryApplyChanges on an `AddDocument` to SampleLib would
        // reserialize OTHER csprojs — adding BOM, flipping LF→CRLF, collapsing blank lines — even
        // though those projects weren't touched. After the fix, only the target file is added and
        // every csproj is byte-identical to its pre-apply state.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var workspaceId = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None)
            .ContinueWith(t => t.Result.WorkspaceId);

        try
        {
            // Snapshot all csprojs in the solution directory BEFORE the apply. We enumerate from
            // disk rather than from the workspace so we also catch non-project files like
            // Directory.Build.props (not a csproj but included in the copy by CopyRepositorySupportFiles).
            var csprojPaths = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
            Assert.IsTrue(csprojPaths.Length >= 2, $"Multi-project regression requires ≥2 csprojs; found {csprojPaths.Length}.");

            var preApply = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var csproj in csprojPaths)
            {
                preApply[csproj] = await File.ReadAllBytesAsync(csproj);
            }

            // Target: add one file to SampleLib. The other 3 csprojs should not be touched.
            var sampleLibProject = WorkspaceManager.GetCurrentSolution(workspaceId).Projects
                .FirstOrDefault(p => string.Equals(p.Name, "SampleLib", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("SampleLib project missing from solution.");
            var newFilePath = Path.Combine(solutionDir, "SampleLib", "CsprojDriftGuard_Generated.cs");

            var previewDto = await FileOperationService.PreviewCreateFileAsync(
                workspaceId,
                new CreateFileDto(
                    ProjectName: sampleLibProject.Name,
                    FilePath: newFilePath,
                    Content: "namespace SampleLib;\n\npublic static class CsprojDriftGuard\n{\n    public const string Marker = \"csproj-reserialization\";\n}\n"),
                CancellationToken.None);

            await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);

            Assert.IsTrue(File.Exists(newFilePath), "The generated file must be on disk after apply.");

            // Assert: every csproj is byte-identical to its pre-apply snapshot. If any csproj
            // drifted (BOM injection, line-ending flip, blank-line collapse) the test fails with
            // a diagnostic naming the affected file.
            var drift = new List<string>();
            foreach (var csproj in csprojPaths)
            {
                var after = await File.ReadAllBytesAsync(csproj);
                if (!preApply[csproj].AsSpan().SequenceEqual(after))
                {
                    drift.Add(
                        $"{Path.GetRelativePath(solutionDir, csproj)}: " +
                        $"pre-apply={preApply[csproj].Length} bytes, post-apply={after.Length} bytes" +
                        DescribeLeadingBomFlip(preApply[csproj], after) +
                        DescribeLineEndingFlip(preApply[csproj], after));
                }
            }

            if (drift.Count > 0)
            {
                Assert.Fail(
                    "csproj-reserialization-msbuildworkspace: TryApplyChanges reserialized csprojs that were NOT part of the document-set change. " +
                    "All csprojs must be byte-identical before and after apply when only one project's document set changes.\n" +
                    string.Join("\n", drift));
            }

            // Sanity: the in-memory workspace DID get the new document.
            var solutionAfter = WorkspaceManager.GetCurrentSolution(workspaceId);
            var sampleLibAfter = solutionAfter.Projects.First(p => string.Equals(p.Name, "SampleLib", StringComparison.Ordinal));
            Assert.IsTrue(
                sampleLibAfter.Documents.Any(d => string.Equals(d.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)),
                "The in-memory workspace must still contain the added document after the csproj-drift restore pass.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static string DescribeLeadingBomFlip(byte[] before, byte[] after)
    {
        var beforeHasBom = before.Length >= 3 && before[0] == 0xEF && before[1] == 0xBB && before[2] == 0xBF;
        var afterHasBom = after.Length >= 3 && after[0] == 0xEF && after[1] == 0xBB && after[2] == 0xBF;
        if (beforeHasBom == afterHasBom)
        {
            return string.Empty;
        }
        return afterHasBom ? " [BOM added]" : " [BOM removed]";
    }

    private static string DescribeLineEndingFlip(byte[] before, byte[] after)
    {
        var beforeHasCrLf = before.AsSpan().IndexOf((byte)0x0D) >= 0;
        var afterHasCrLf = after.AsSpan().IndexOf((byte)0x0D) >= 0;
        if (beforeHasCrLf == afterHasCrLf)
        {
            return string.Empty;
        }
        return afterHasCrLf ? " [line-endings LF→CRLF]" : " [line-endings CRLF→LF]";
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
