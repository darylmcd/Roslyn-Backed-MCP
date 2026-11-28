using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkNativeBuildOutputAdmissionTests : ForkNativeAdmissionTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow("reference", false, true)]
    [DataRow("reference", true, true)]
    [DataRow("reference", true, false)]
    [DataRow("symbols", false, true)]
    [DataRow("symbols", true, true)]
    [DataRow("symbols", true, false)]
    [DataRow("documentation", false, true)]
    [DataRow("documentation", true, true)]
    [DataRow("documentation", true, false)]
    [DataRow("additional", false, true)]
    [DataRow("additional", true, true)]
    [DataRow("additional", true, false)]
    [DataRow("winmd", false, true)]
    [DataRow("winmd", true, true)]
    [DataRow("winmd", true, false)]
    [DataRow("satellite", false, true)]
    [DataRow("satellite", true, true)]
    [DataRow("satellite", true, false)]
    [DataRow("serialization", false, true)]
    [DataRow("serialization", true, true)]
    [DataRow("serialization", true, false)]
    [DataRow("disabled", true, true)]
    public async Task NativeBuildOutputBranches_UseActualCompiledInputsAndNativeConditions(string branch, bool guarded, bool sourceOutput)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Consumer.csproj");
        fixture.WriteProject(project, "<PropertyGroup><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup>");
        var outputDirectory = sourceOutput ? fixture.Source : fixture.PathInFork("allowed");
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, branch == "reference" ? "reference.dll" : branch == "symbols" ? "symbols.pdb" : branch == "documentation" ? "documentation.xml" : branch == "satellite" ? "en/Consumer.resources.dll" : "Consumer.dll");
        var properties = new XElement("PropertyGroup", new XElement("OutDir", outputDirectory),
            new XElement("CopyBuildOutputToOutputDirectory", branch == "reference" ? "true" : "false"),
            new XElement("SkipCopyBuildProduct", branch == "disabled" ? "true" : "false"),
            new XElement("CopyOutputSymbolsToOutputDirectory", branch == "symbols" ? "true" : "false"),
            new XElement("CopyDocumentationFileToOutputDirectory", branch == "documentation" ? "true" : "false"));
        var items = new XElement("ItemGroup");
        if (branch == "reference")
        {
            properties.Element("OutDir")!.Value += Path.DirectorySeparatorChar;
            properties.Add(new XElement("TargetRefPath", output));
            // Isolate the native reference copy from the preceding main-product copy.
            items.Add(new XElement("IntermediateAssembly", new XAttribute("Remove", "@(IntermediateAssembly)")));
        }
        else if (branch == "symbols")
            items.Add(new XElement("_DebugSymbolsOutputPath", new XAttribute("Remove", "@(_DebugSymbolsOutputPath)")),
                new XElement("_DebugSymbolsOutputPath", new XAttribute("Include", output)));
        else if (branch == "documentation")
            items.Add(new XElement("FinalDocFile", new XAttribute("Remove", "@(FinalDocFile)")), new XElement("FinalDocFile", new XAttribute("Include", output)));
        else if (branch == "serialization")
        {
            properties.Element("OutDir")!.Value += Path.DirectorySeparatorChar;
            properties.Add(new XElement("_SGenDllCreated", "true"), new XElement("_SGenDllName", "Consumer.dll"));
        }
        else if (branch == "satellite")
        {
            properties.Element("OutDir")!.Value += Path.DirectorySeparatorChar;
            items.Add(new XElement("IntermediateSatelliteAssembliesWithTargetPath", new XAttribute("Include", "@(IntermediateAssembly)"),
                new XAttribute("Culture", "en"), new XAttribute("TargetPath", "en/Consumer.resources.dll")));
        }
        else if (branch != "disabled")
            items.Add(new XElement(branch == "winmd" ? "WinMDExpArtifacts" : "NativeReferenceFile", new XAttribute("Include", "@(IntermediateAssembly)")));
        var hook = new XElement("Target", new XAttribute("Name", "LateBuildOutputSelection"), new XAttribute("BeforeTargets", "CopyFilesToOutputDirectory"), properties, items);
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project", hook).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        var config = guarded ? ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token) : null;
        using var execution = config is null ? null : await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System, timeout.Token);
        string[] arguments = ["build", project, "--no-restore", "--nologo", "-nr:false", "-p:UseSharedCompilation=false"];
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project, execution?.Arguments(arguments) ?? arguments, timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        if (branch == "disabled")
        {
            Assert.IsTrue(result.Succeeded, result.Describe());
            Assert.IsFalse(File.Exists(output), "Native disabled branches must not cause false admission refusals or copies.");
        }
        else if (guarded && sourceOutput)
        {
            Assert.IsFalse(result.Succeeded, result.Describe());
            Assert.IsFalse(File.Exists(output));
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.Describe());
            var intermediate = fixture.PathInFork("obj/Debug/net10.0/" + (branch == "reference" ? "refint/Consumer.dll" : branch == "symbols" ? "Consumer.pdb" : branch == "documentation" ? "Consumer.xml" : "Consumer.dll"));
            CollectionAssert.AreEqual(File.ReadAllBytes(intermediate), File.ReadAllBytes(output));
        }
    }

    [TestMethod]
    [DataRow("None", false, "plain", false)]
    [DataRow("None", false, "plain", true)]
    [DataRow("None", true, "plain", false)]
    [DataRow("None", true, "plain", true)]
    [DataRow("Compile", false, "plain", false)]
    [DataRow("Compile", false, "plain", true)]
    [DataRow("None", false, "rooted", false)]
    [DataRow("None", false, "rooted", true)]
    [DataRow("None", false, "traversal", false)]
    [DataRow("None", false, "traversal", true)]
    [DataRow("None", false, "hash", false)]
    [DataRow("None", false, "hash", true)]
    [DataRow("None", false, "package-root", false)]
    [DataRow("None", false, "package-root", true)]
    [DataRow("None", false, "version-root", false)]
    [DataRow("None", false, "version-root", true)]
    [DataRow("None", false, "junction-parent", false)]
    [DataRow("None", false, "junction-source", true)]
    public async Task NativePreprocessing_FileWritesMembershipAndPaths_MatchOrdinaryEffects(string action, bool copy, string shape, bool sourceOutput)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var package = Path.Combine(fixture.Root, "package");
        var feed = Path.Combine(fixture.Root, "feed");
        Directory.CreateDirectory(Path.Combine(package, "contentFiles/any/net10.0"));
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(package, "Native.Content.nuspec"), """
            <package><metadata><id>Native.Content</id><version>1.0.0</version><authors>Test</authors><description>Native content</description><contentFiles><files include="any/net10.0/probe.cs.pp" buildAction="None" copyToOutput="false" /></contentFiles></metadata></package>
            """);
        File.WriteAllText(Path.Combine(package, "contentFiles/any/net10.0/probe.cs.pp"), "// native $rootnamespace$");
        System.IO.Compression.ZipFile.CreateFromDirectory(package, Path.Combine(feed, "Native.Content.1.0.0.nupkg"));
        var project = fixture.PathInFork("Content.csproj");
        var destination = Path.Combine(sourceOutput ? fixture.Source : fixture.Fork, "native");
        var originalRoot = destination;
        if (shape.StartsWith("junction", StringComparison.Ordinal))
        {
            var alias = fixture.PathInFork("alias");
            WorkspaceForkGraphReplayTests.CreateLink(alias, fixture.Source);
            originalRoot = shape == "junction-parent" ? Path.Combine(alias, "..", "native") : Path.Combine(alias, "native");
        }
        var pp = shape switch { "rooted" => Path.Combine(destination, "probe.cs"), "traversal" => "../probe.cs", "hash" => "../../../probe.cs", _ => "probe.cs" };
        fixture.WriteProject(project, new XElement("PropertyGroup",
                new XElement("RestoreSources", feed), new XElement("RestorePackagesPath", Path.Combine(fixture.Root, "packages")),
                new XElement("ContentPreprocessorOutputDirectory", originalRoot), new XElement("RootNamespace", "NativeMatrix")) +
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Native.Content"), new XAttribute("Version", "1.0.0"))).ToString());
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "LateNativeContentMetadata"), new XAttribute("BeforeTargets", "RunProduceContentAssets"),
                new XElement("ItemGroup", new XElement("_ContentFilesToPreprocess", new XElement("CopyToOutput", copy ? "true" : "false"),
                    new XElement("BuildAction", action), new XElement("PPOutputPath", pp),
                    shape == "package-root" ? new XElement("NuGetPackageId", destination) : null,
                    shape == "version-root" ? new XElement("NuGetPackageVersion", destination) : null)),
                new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(fixture.Evidence, "hooks.txt")), new XAttribute("Lines", "once"))),
            new XElement("Target", new XAttribute("Name", "CaptureNativeContentMembership"), new XAttribute("AfterTargets", "RunProduceContentAssets"),
                new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(fixture.Evidence, "membership.txt")),
                    new XAttribute("Lines", "@(_ContentCopyLocalItems->'copy=%(FullPath)|%(TargetPath)|%(CopyToOutputDirectory)');@(_ProcessedContentItems->'processed=%(FullPath)|%(ProcessedItemType)');@(FileWrites->'sdk=%(FullPath)')"), new XAttribute("Overwrite", "true")))).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        string[] arguments = ["msbuild", project, "-nologo", "-nr:false", "-t:ResolvePackageAssets;RunProduceContentAssets"];
        var ordinary = await DotnetCommandRunner.RunAsync(fixture.Fork, project, arguments, timeout.Token);
        Assert.IsTrue(ordinary.Succeeded, ordinary.Describe());
        var product = Directory.GetFiles(destination, "probe.cs", SearchOption.AllDirectories).Single();
        var bytes = File.ReadAllBytes(product);
        Assert.AreEqual("// native NativeMatrix", File.ReadAllText(product));
        var membership = string.Join("\n", File.ReadAllLines(Path.Combine(fixture.Evidence, "membership.txt")));
        if (!copy && action == "None") Assert.AreEqual(string.Empty, membership, "Native FileWrites-only products must not be inferred from SDK copy/processed/FileWrites arrays.");
        File.Delete(product);
        File.Delete(Path.Combine(fixture.Evidence, "hooks.txt"));
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        using var execution = await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System, timeout.Token);
        var candidate = await DotnetCommandRunner.RunAsync(fixture.Fork, project, execution.Arguments(arguments), timeout.Token);
        execution.Complete();
        TestContext.WriteLine(candidate.StdOut + candidate.StdErr);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(fixture.Evidence, "hooks.txt")).Length);
        if (sourceOutput)
        {
            Assert.IsFalse(candidate.Succeeded);
            Assert.IsFalse(File.Exists(product));
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(candidate.Succeeded, candidate.Describe());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(product));
            Assert.AreEqual(membership, string.Join("\n", File.ReadAllLines(Path.Combine(fixture.Evidence, "membership.txt"))));
        }
    }
}
