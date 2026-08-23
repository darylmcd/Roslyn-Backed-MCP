using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-mtp-native-test-run: TUnit is entirely built on Microsoft.Testing.Platform (MTP) and
/// never registers with the classic VSTest adapter (confirmed via
/// https://learn.microsoft.com/dotnet/core/testing/#testing-tools and by direct repro — the
/// legacy VSTest-mode MTP bridge is hard-removed on the .NET 10 SDK). TestRunnerService uses
/// ProjectMetadataParser.RequiresMtpNativeExecution to route such a project to the MTP-native
/// dotnet test argument shape instead of today's classic --logger/--filter invocation.
/// </summary>
[TestClass]
public sealed class ProjectMetadataParserRequiresMtpNativeExecutionTests
{
    [TestMethod]
    public void RequiresMtpNativeExecution_TUnitPackageReference_ReturnsTrue()
    {
        var document = XDocument.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.65.38" />
              </ItemGroup>
            </Project>
            """);

        Assert.IsTrue(
            ProjectMetadataParser.RequiresMtpNativeExecution(document),
            "A project referencing the TUnit package must require MTP-native dotnet test execution.");
    }

    [TestMethod]
    public void RequiresMtpNativeExecution_XUnitPackageReference_ReturnsFalse()
    {
        // xUnit stays VSTest-compatible via Microsoft.Testing.Extensions.VSTestBridge, so it
        // must keep using today's classic --logger/--filter invocation.
        var document = XDocument.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.IsFalse(ProjectMetadataParser.RequiresMtpNativeExecution(document));
    }

    [TestMethod]
    public void RequiresMtpNativeExecution_NullDocument_ReturnsFalse()
    {
        Assert.IsFalse(ProjectMetadataParser.RequiresMtpNativeExecution((XDocument?)null));
    }
}
