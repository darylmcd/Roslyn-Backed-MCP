using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkGraphPreparationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("IntermediateOutputPath")]
    [DataRow("BaseIntermediateOutputPath")]
    [DataRow("MSBuildProjectExtensionsPath")]
    [DataRow("ProjectAssetsFile")]
    [DataRow("RestoreOutputPath")]
    [DataRow("BaseOutputPath")]
    [DataRow("OutputPath")]
    [DataRow("OutDir")]
    [DataRow("TargetDir")]
    [DataRow("TargetPath")]
    [DataRow("DocumentationFile")]
    [DataRow("GeneratedAssemblyInfoFile")]
    [DataRow("GeneratedGlobalUsingsFile")]
    [DataRow("CompilerGeneratedFilesOutputPath")]
    [DataRow("ErrorLog")]
    [DataRow("PdbFile")]
    [DataRow("RestorePackagesPath")]
    [DataRow("NuGetLockFilePath")]
    [DataRow("PackageOutputPath")]
    public void SourceDestination_IsRefusedWithoutRequiringExistence(string property)
    {
        using var fixture = new GraphFixture();
        fixture.Write(new XElement("PropertyGroup", new XElement(property, fixture.SourcePath("not-created"))));
        AssertIsolationRefusal(fixture);
        Assert.IsFalse(Directory.Exists(fixture.SourcePath("not-created")));
    }

    [TestMethod]
    [DataRow("Compile")]
    [DataRow("AdditionalFiles")]
    [DataRow("AdditionalDocuments")]
    [DataRow("Analyzer")]
    [DataRow("EmbeddedResource")]
    [DataRow("Content")]
    [DataRow("None")]
    [DataRow("ReferencePath")]
    [DataRow("IntermediateRefAssembly")]
    [DataRow("CustomPhysicalInput")]
    public void PhysicalInput_IsRefused(string itemType)
    {
        using var fixture = new GraphFixture();
        fixture.Write(new XElement("ItemGroup", new XElement(itemType,
            new XAttribute("Include", fixture.SourcePath("input.cs")))));
        AssertIsolationRefusal(fixture);
    }

    [TestMethod]
    public void HintPathAndContributingImport_AreRefused()
    {
        using var fixture = new GraphFixture();
        fixture.Write(new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "LogicalAssembly"),
            new XElement("HintPath", fixture.SourcePath("assembly.dll")))));
        AssertIsolationRefusal(fixture);
        File.WriteAllText(fixture.SourcePath("source.props"), "<Project />");
        fixture.Write(new XElement("Import", new XAttribute("Project", fixture.SourcePath("source.props"))));
        AssertIsolationRefusal(fixture);
    }

    [TestMethod]
    public void EscapedSemicolon_IsOnePhysicalPath_AndBookkeepingIsNotAFile()
    {
        using var fixture = new GraphFixture();
        fixture.Write(new XElement("ItemGroup",
            new XElement("PackageReference", new XAttribute("Include", fixture.SourcePath("Logical.Package"))),
            new XElement("FrameworkReference", new XAttribute("Include", fixture.SourcePath("Logical.Framework"))),
            new XElement("Reference", new XAttribute("Include", "System.Runtime, Version=1.0.0.0"))),
            new XElement("Import", new XAttribute("Project", fixture.SourcePath("missing.props")), new XAttribute("Condition", "false")),
            new XElement("ItemGroup", new XElement("SourceRoot", new XAttribute("Include", fixture.Source)),
                new XElement("_AllDirectoriesAbove", new XAttribute("Include", fixture.Source)),
                new XElement("PotentialEditorConfigFiles", new XAttribute("Include", fixture.SourcePath("missing.editorconfig"))),
                new XElement("GlobalAnalyzerConfigFiles", new XAttribute("Include", fixture.SourcePath("missing.globalconfig")))));
        fixture.Validate();
        fixture.Write(new XElement("PropertyGroup", new XElement("IntermediateOutputPath", fixture.SourcePath("semi%3Bcolon"))));
        AssertIsolationRefusal(fixture);
    }

    [TestMethod]
    public void ExternalAndCurrentForkAliases_AreAllowed_SourceAliasAndParentTraversalAreRefused()
    {
        using var fixture = new GraphFixture();
        var forkAlias = Path.Combine(fixture.Root, "fork-alias");
        var outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        var outsideAlias = Path.Combine(fixture.Root, "outside-alias");
        WorkspaceForkGraphReplayTests.CreateLink(forkAlias, fixture.Fork);
        WorkspaceForkGraphReplayTests.CreateLink(outsideAlias, outside);
        fixture.Write(new XElement("PropertyGroup", new XElement("IntermediateOutputPath", Path.Join(forkAlias, "missing")),
            new XElement("OutputPath", Path.Join(outsideAlias, "missing"))));
        fixture.Validate();
        var sourceAlias = Path.Combine(fixture.Root, "source-alias");
        WorkspaceForkGraphReplayTests.CreateLink(sourceAlias, fixture.Source);
        fixture.Write(new XElement("PropertyGroup", new XElement("IntermediateOutputPath", Path.Join(sourceAlias, "missing"))));
        AssertIsolationRefusal(fixture);
        Directory.CreateDirectory(fixture.SourcePath("child"));
        var childAlias = Path.Combine(fixture.Root, "child-alias");
        WorkspaceForkGraphReplayTests.CreateLink(childAlias, fixture.SourcePath("child"));
        fixture.Write(new XElement("PropertyGroup", new XElement("IntermediateOutputPath", Path.Join(childAlias, "..", "missing"))));
        AssertIsolationRefusal(fixture);
    }

    [TestMethod]
    public void RootEqualityAndPrefixSibling_UsePlatformContainment()
    {
        using var fixture = new GraphFixture();
        fixture.Write(new XElement("PropertyGroup", new XElement("OutputPath", fixture.Source + "-sibling")));
        fixture.Validate();
        fixture.Write(new XElement("PropertyGroup", new XElement("OutputPath", fixture.Source)));
        AssertIsolationRefusal(fixture);
        fixture.Write(new XElement("PropertyGroup", new XElement("OutputPath", fixture.Source.ToUpperInvariant())));
        if (OperatingSystem.IsWindows()) AssertIsolationRefusal(fixture);
        else fixture.Validate();
    }

    [TestMethod]
    public void ReferencePropertiesAndEveryFramework_AreInspectedBeforeCollapse()
    {
        using var fixture = new GraphFixture();
        var hidden = Path.Combine(fixture.Fork, "Hidden.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFrameworks", "net8.0;net10.0")),
            new XElement("PropertyGroup", new XAttribute("Condition", "'$(TargetFramework)' == 'net10.0' and '$(Flavor)' == 'Escape'"),
                new XElement("IntermediateOutputPath", fixture.SourcePath("framework-output"))))).Save(hidden);
        fixture.Write(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "Hidden.csproj"),
            new XElement("AdditionalProperties", "Flavor=Escape"), new XElement("SetTargetFramework", "TargetFramework=net8.0"))));
        AssertIsolationRefusal(fixture);
    }

    [TestMethod]
    public void CancellationBeforeAndDuringCapture_PreservesCallerCancellation()
    {
        using var fixture = new GraphFixture();
        fixture.Write();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        Assert.ThrowsExactly<OperationCanceledException>(() => ForkGraphPreparation.Validate(fixture.Entry,
            fixture.Source, fixture.Fork, () => { calls++; return TimeSpan.FromMinutes(1); }, cancellation.Token));
        Assert.AreEqual(0, calls);
        using var during = new CancellationTokenSource();
        calls = 0;
        Assert.Throws<OperationCanceledException>(() => ForkGraphPreparation.Validate(fixture.Entry,
            fixture.Source, fixture.Fork, () => { if (++calls == 3) during.Cancel(); return TimeSpan.FromMinutes(1); }, during.Token));
    }

    [TestMethod]
    public void EvaluationAndAmbiguousPathFailures_AreSanitizedRefusals()
    {
        using var fixture = new GraphFixture();
        fixture.Write(new XElement("Import", new XAttribute("Project", "PRIVATE-missing.props")));
        var exception = Assert.Throws<InvalidOperationException>(fixture.Validate);
        Assert.IsInstanceOfType<IPublicMessageException>(exception);
        Assert.IsFalse(exception.Message.Contains("PRIVATE", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
        {
            fixture.Write(new XElement("PropertyGroup", new XElement("OutputPath", "C:PRIVATE-ambiguous")));
            exception = Assert.Throws<InvalidOperationException>(fixture.Validate);
            Assert.IsInstanceOfType<IPublicMessageException>(exception);
            Assert.IsFalse(exception.Message.Contains("PRIVATE", StringComparison.Ordinal));
        }
    }

    private static void AssertIsolationRefusal(GraphFixture fixture)
    {
        var exception = Assert.Throws<InvalidOperationException>(fixture.Validate);
        Assert.IsInstanceOfType<IPublicMessageException>(exception);
        StringAssert.Contains(exception.Message, "source workspace");
    }

    private sealed class GraphFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(TestTempRoot.Current, "fork-graph-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Root, "source");
        internal string Fork => Path.Combine(Source, ".roslynmcp", "forks", "current");
        internal string Entry => Path.Combine(Fork, "Entry.proj");

        internal GraphFixture()
        {
            Directory.CreateDirectory(Fork);
        }

        internal string SourcePath(string name) => Path.Combine(Source, name);
        internal void Write(params object[] content) => new XDocument(new XElement("Project", content)).Save(Entry);
        internal void Validate() => ForkGraphPreparation.Validate(Entry, Source, Fork, () => TimeSpan.FromMinutes(1), CancellationToken.None);
        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }
}
