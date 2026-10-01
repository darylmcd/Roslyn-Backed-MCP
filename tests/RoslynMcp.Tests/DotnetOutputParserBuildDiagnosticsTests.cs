using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DotnetOutputParserBuildDiagnosticsTests
{
    [TestMethod]
    public void ParseBuildDiagnostics_LocationlessNuGetError_IsReported()
    {
        var diagnostic = DotnetOutputParser.ParseBuildDiagnostics(
            @"D:\repo\SampleLib.Tests.csproj : error NU1201: Project SampleLib is not compatible with net10.0. [D:\repo\Sample.slnx]").Single();

        Assert.AreEqual("NU1201", diagnostic.Id);
        Assert.AreEqual("Error", diagnostic.Severity);
        Assert.AreEqual("Build", diagnostic.Category);
        Assert.AreEqual(@"D:\repo\SampleLib.Tests.csproj", diagnostic.FilePath);
        Assert.AreEqual("Project SampleLib is not compatible with net10.0.", diagnostic.Message);
        Assert.IsNull(diagnostic.StartLine);
        Assert.IsNull(diagnostic.StartColumn);
        Assert.IsNull(diagnostic.Location);
    }

    [TestMethod]
    public void ParseBuildDiagnostics_MsBuildToolError_IsReportedWithoutProjectSuffix()
    {
        var diagnostic = DotnetOutputParser.ParseBuildDiagnostics(
            "MSBUILD : error MSB1009: Project file does not exist.").Single();

        Assert.AreEqual("MSB1009", diagnostic.Id);
        Assert.AreEqual("MSBUILD", diagnostic.FilePath);
        Assert.AreEqual("Project file does not exist.", diagnostic.Message);
    }

    [TestMethod]
    public void ParseBuildDiagnostics_LocationlessWarning_IsReportedAsWarning()
    {
        var diagnostic = DotnetOutputParser.ParseBuildDiagnostics(
            "Sample.csproj : warning NU1603: Sample depends on Foo (>= 1.0.0) but Foo 1.0.0 was not found.").Single();

        Assert.AreEqual("NU1603", diagnostic.Id);
        Assert.AreEqual("Warning", diagnostic.Severity);
    }

    [TestMethod]
    public void ParseBuildDiagnostics_LocatedLine_ParsesOnceWithPosition()
    {
        var diagnostic = DotnetOutputParser.ParseBuildDiagnostics(
            "sample.cs(2,3): error CS1002: ; expected [Sample.csproj]").Single();

        Assert.AreEqual("CS1002", diagnostic.Id);
        Assert.AreEqual("sample.cs", diagnostic.FilePath);
        Assert.AreEqual(2, diagnostic.StartLine);
        Assert.AreEqual(3, diagnostic.StartColumn);
    }

    [TestMethod]
    public void ParseBuildDiagnostics_ProseWithoutDiagnosticShape_IsIgnored()
    {
        var diagnostics = DotnetOutputParser.ParseBuildDiagnostics(
            string.Join(
                Environment.NewLine,
                "Build FAILED.",
                "Assertion : error expected but none was thrown",
                "Test run : error nu1201: lowercase id is not a diagnostic id",
                "    0 Error(s)"));

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public void ParseBuildDiagnostics_LocationlessRetryDuplicate_IsCollapsedButDistinctMessagesAreKept()
    {
        var output = string.Join(
            Environment.NewLine,
            "Sample.csproj : error NU1101: Unable to find package Foo. [Sample.slnx]",
            "Sample.csproj : error NU1101: Unable to find package Foo. [Sample.slnx]",
            "Sample.csproj : error NU1101: Unable to find package Bar. [Sample.slnx]");

        var diagnostics = DotnetOutputParser.ParseBuildDiagnostics(output);

        Assert.AreEqual(2, diagnostics.Count);
        Assert.IsTrue(diagnostics.Any(d => d.Message.EndsWith("Foo.", StringComparison.Ordinal)));
        Assert.IsTrue(diagnostics.Any(d => d.Message.EndsWith("Bar.", StringComparison.Ordinal)));
    }
}
