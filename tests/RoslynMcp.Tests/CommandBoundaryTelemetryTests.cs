using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>Allowlist projections used by the dependency-boundary records.</summary>
[TestClass]
public sealed class CommandBoundaryTelemetryTests
{
    [TestMethod]
    [DataRow("build")]
    [DataRow("test")]
    [DataRow("restore")]
    [DataRow("msbuild")]
    [DataRow("--version")]
    [DataRow("-h")]
    public void Verb_AcceptsBareVerbsAndOptionNames(string first)
    {
        Assert.AreEqual(first, CommandBoundaryTelemetry.Verb([first, "--filter", "secret"]));
    }

    [TestMethod]
    [DataRow("C:/private/App.slnx")]
    [DataRow("/home/user/App.slnx")]
    [DataRow("Build")]
    [DataRow("build\n")]
    [DataRow("build ")]
    [DataRow("-p:Root=C:/x")]
    [DataRow("a1234567890123456789012345678901234")]
    [DataRow("")]
    [DataRow(null)]
    public void Verb_ReturnsOtherForAnythingThatIsNotABareVerb(string? first)
    {
        Assert.AreEqual("other", CommandBoundaryTelemetry.Verb([first!]));
    }

    [TestMethod]
    public void Verb_AcceptsTheLongestAllowedVerbAndNamesAnEmptyList()
    {
        var longest = "a" + new string('b', 31);
        Assert.AreEqual(longest, CommandBoundaryTelemetry.Verb([longest]));
        Assert.AreEqual("none", CommandBoundaryTelemetry.Verb([]));
    }

    [TestMethod]
    public void TargetKind_ClassifiesWithoutNamingTheTarget()
    {
        Assert.AreEqual("solution", CommandBoundaryTelemetry.TargetKind("C:/private/App.SLNX"));
        Assert.AreEqual("solution", CommandBoundaryTelemetry.TargetKind("App.sln"));
        Assert.AreEqual("project", CommandBoundaryTelemetry.TargetKind("C:/private/App.csproj"));
        Assert.AreEqual("project", CommandBoundaryTelemetry.TargetKind("App.fsproj"));
        Assert.AreEqual("directory", CommandBoundaryTelemetry.TargetKind(Environment.CurrentDirectory));
        Assert.AreEqual("other", CommandBoundaryTelemetry.TargetKind("C:/private/missing"));
        Assert.AreEqual("other", CommandBoundaryTelemetry.TargetKind(""));
        Assert.AreEqual("other", CommandBoundaryTelemetry.TargetKind(null!));
    }
}
