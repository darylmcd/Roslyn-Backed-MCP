using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-test-project-classification: TUnit ships its own Microsoft.Testing.Platform host
/// instead of the classic VSTest adapter, so a TUnit project references the "TUnit" package
/// but never "Microsoft.NET.Test.Sdk" and doesn't stamp &lt;IsTestProject&gt; itself. Before this
/// fix, ProjectMetadataParser.IsTestProject returned false for such projects, making them
/// invisible to test_discover/test_run (reported as "0 test projects found").
/// </summary>
[TestClass]
public sealed class ProjectMetadataParserIsTestProjectTests
{
    [TestMethod]
    public void IsTestProject_TUnitPackageReference_ReturnsTrue()
    {
        var document = XDocument.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="0.1.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.IsTrue(
            ProjectMetadataParser.IsTestProject(document),
            "A project referencing the TUnit package must be classified as a test project.");
    }

    [TestMethod]
    public void IsTestProject_PlainLibraryWithNoTestPackages_ReturnsFalse()
    {
        var document = XDocument.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Assert.IsFalse(
            ProjectMetadataParser.IsTestProject(document),
            "A plain library project with no test SDK/package markers must not be classified as a test project.");
    }

    [TestMethod]
    public void IsTestProject_NullDocument_ReturnsFalse()
    {
        Assert.IsFalse(ProjectMetadataParser.IsTestProject((XDocument?)null));
    }
}
