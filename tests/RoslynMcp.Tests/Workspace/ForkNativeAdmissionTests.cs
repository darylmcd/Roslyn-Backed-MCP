using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkNativeAdmissionTests : IsolatedWorkspaceTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task NativeBuildHost_LateChild_FirstMetadataAndCachedCopy_AreGuarded(bool guarded, bool quoted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(quoted);
        var parent = fixture.PathInFork("Parent.csproj");
        var child = fixture.PathInFork("Child.custombuild");
        File.WriteAllText(fixture.PathInFork("input.txt"), "native copied bytes");
        fixture.WriteProject(child, """
            <PropertyGroup>
              <EvaluationIdentity>$([System.Guid]::NewGuid())</EvaluationIdentity>
              <LanguageTargets>$(MSBuildToolsPath)/Microsoft.CSharp.targets</LanguageTargets>
            </PropertyGroup>
            <Target Name="ObserveFirstMetadata" BeforeTargets="GetTargetFrameworks">
              <WriteLinesToFile File="$(ProofEvidence)/metadata.txt" Lines="$(EvaluationIdentity)|$(RoslynMcpForkConfig)|$(DesignTimeBuild)" Overwrite="false" />
            </Target>
            <Target Name="ObserveCachedBuild" BeforeTargets="PrepareForBuild">
              <WriteLinesToFile File="$(ProofEvidence)/build.txt" Lines="$(EvaluationIdentity)|$(RoslynMcpForkConfig)|$(DesignTimeBuild)" Overwrite="false" />
            </Target>
            <ItemGroup><_SourceItemsToCopyToOutputDirectoryAlways Include="input.txt" TargetPath="safe.txt" /></ItemGroup>
            <Target Name="MutateCopy" BeforeTargets="_CopyOutOfDateSourceItemsToOutputDirectoryAlways">
              <ItemGroup><_SourceItemsToCopyToOutputDirectoryAlways Update="input.txt" TargetPath="../../protected.txt" /></ItemGroup>
            </Target>
            """);
        fixture.WriteProject(parent, """
            <PropertyGroup><LateBoundary>_GetProjectReferenceTargetFrameworkProperties</LateBoundary></PropertyGroup>
            <Target Name="LateCustomReference" BeforeTargets="$(LateBoundary)">
              <ItemGroup>
                <_MSBuildProjectReferenceExistent Include="Child.custombuild" />
                <ProjectReferenceWithConfiguration Include="Child.custombuild" BuildReference="true" ReferenceOutputAssembly="true" />
              </ItemGroup>
            </Target>
            <Target Name="ExerciseNativeChild" BeforeTargets="ResolveReferences" DependsOnTargets="PrepareProjectReferences">
              <MSBuild Projects="@(_MSBuildProjectReferenceExistent)" Targets="PrepareForBuild;_CopyOutOfDateSourceItemsToOutputDirectoryAlways" BuildInParallel="true" />
            </Target>
            """);
        await fixture.RestoreAsync(child, DotnetCommandRunner, timeout.Token);
        await fixture.RestoreAsync(parent, DotnetCommandRunner, timeout.Token);
        Assert.IsFalse(File.ReadAllText(child).Contains(ForkProjectImports.Label, StringComparison.Ordinal));
        if (guarded) ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [parent], timeout.Token);
        // The custom-extension child must be installed at the actual earliest query,
        // rather than an initial glob or a disk change after cached evaluation.
        Assert.IsFalse(File.ReadAllText(child).Contains(ForkProjectImports.Label, StringComparison.Ordinal));
        string? workspaceId = null;
        Exception? refusal = null;
        try
        {
            try
            {
                var status = await WorkspaceManager.LoadAsync(parent,
                    new Dictionary<string, string> { ["ProofEvidence"] = fixture.Evidence }, timeout.Token);
                workspaceId = status.WorkspaceId;
            }
            catch (InvalidOperationException ex) when (ex is IPublicMessageException) { refusal = ex; }
            var metadata = File.ReadAllLines(Path.Combine(fixture.Evidence, "metadata.txt")).Single().Split('|');
            var build = File.ReadAllLines(Path.Combine(fixture.Evidence, "build.txt")).Single().Split('|');
            Assert.AreEqual(metadata[0], build[0], "The real first metadata and later native copy continuation must reuse one evaluated child.");
            Assert.AreEqual("true", metadata[2], true, "This must execute in the actual design-time BuildHost.");
            Assert.AreEqual("true", build[2], true);
            TestContext.WriteLine("NATIVE_METADATA=" + string.Join('|', metadata));
            TestContext.WriteLine("NATIVE_BUILD=" + string.Join('|', build));
            if (guarded)
            {
                Assert.IsInstanceOfType<IPublicMessageException>(refusal);
                Assert.IsFalse(File.Exists(fixture.ProtectedOutput), "Admission must precede the real SDK Copy.");
                Assert.AreEqual(1, XDocument.Load(child).Root!.Elements("Import")
                    .Count(element => (string?)element.Attribute("Label") == ForkProjectImports.Label));
                Assert.IsFalse(string.IsNullOrEmpty(metadata[1]), "The import must exist before first child evaluation.");
            }
            else
            {
                Assert.IsNull(refusal);
                Assert.AreEqual("native copied bytes", File.ReadAllText(fixture.ProtectedOutput),
                    "The ordinary unchanged native SDK Copy is the independent source-effect authority.");
                Assert.AreEqual(string.Empty, metadata[1]);
            }
        }
        finally
        {
            if (workspaceId is not null) WorkspaceManager.Close(workspaceId);
        }
    }


    [TestMethod]
    [DataRow("Always", false, true)]
    [DataRow("Always", true, true)]
    [DataRow("PreserveNewest", false, true)]
    [DataRow("PreserveNewest", true, true)]
    [DataRow("IfDifferent", false, true)]
    [DataRow("IfDifferent", true, true)]
    [DataRow("CopyLocal", false, true)]
    [DataRow("CopyLocal", true, true)]
    [DataRow("AppConfig", false, true)]
    [DataRow("AppConfig", true, true)]
    [DataRow("Cache", false, true)]
    [DataRow("Cache", true, true)]
    [DataRow("Always", true, false)]
    [DataRow("PreserveNewest", true, false)]
    [DataRow("IfDifferent", true, false)]
    [DataRow("CopyLocal", true, false)]
    [DataRow("AppConfig", true, false)]
    [DataRow("Cache", true, false)]
    [DataRow("BuildProduct", false, true)]
    [DataRow("BuildProduct", true, true)]
    [DataRow("BuildProduct", true, false)]
    [DataRow("BuildProductNoSeparator", false, true)]
    [DataRow("BuildProductNoSeparator", true, true)]
    [DataRow("BuildProductNoSeparator", true, false)]
    [DataRow("BackslashAlways", false, true)]
    [DataRow("BackslashAlways", true, true)]
    [DataRow("BackslashAlways", true, false)]
    public async Task NativeConsumers_ActualLateMutatedPaths_RefuseBeforeEffect(string consumer, bool guarded, bool sourceOutput)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Consumer.csproj");
        var target = consumer switch
        {
            "Always" or "BackslashAlways" => "_CopyOutOfDateSourceItemsToOutputDirectoryAlways",
            "PreserveNewest" => "_CopyOutOfDateSourceItemsToOutputDirectory",
            "IfDifferent" => "_CopyDifferingSourceItemsToOutputDirectory",
            "CopyLocal" => "_CopyFilesMarkedCopyLocal",
            "AppConfig" => "_CopyAppConfigFile",
            "BuildProduct" or "BuildProductNoSeparator" => "CopyFilesToOutputDirectory",
            _ => "ResolvePackageAssets",
        };
        var itemName = consumer switch
        {
            "Always" or "BackslashAlways" => "_SourceItemsToCopyToOutputDirectoryAlways",
            "PreserveNewest" => "_SourceItemsToCopyToOutputDirectory",
            "IfDifferent" => "_SourceItemsToCopyToOutputDirectoryIfDifferent",
            "CopyLocal" => "ReferenceCopyLocalPaths",
            "BuildProduct" or "BuildProductNoSeparator" => "IntermediateAssembly",
            _ => "AppConfigWithTargetPath",
        };
        File.WriteAllText(fixture.PathInFork("input.txt"), "native consumer bytes");
        var protectedOutput = sourceOutput
            ? consumer.StartsWith("BuildProduct", StringComparison.Ordinal) ? Path.Combine(fixture.Source, "Consumer.dll") : consumer == "CopyLocal" ? Path.Combine(fixture.Source, "input.txt") : fixture.ProtectedOutput
            : consumer == "CopyLocal" ? fixture.PathInFork("bin/safe/input.txt") : consumer.StartsWith("BuildProduct", StringComparison.Ordinal) ? fixture.PathInFork("bin/Consumer.dll") : consumer == "BackslashAlways" ? fixture.PathInFork("bin/safe/nested.txt") : fixture.PathInFork("bin/safe.txt");
        var body = consumer.StartsWith("BuildProduct", StringComparison.Ordinal)
            ? new XElement("Target", new XAttribute("Name", "_RoslynMcpForkAuthoredProductHook"),
                new XAttribute("BeforeTargets", target), new XElement("PropertyGroup", new XElement("OutDir",
                    (sourceOutput ? fixture.Source : fixture.PathInFork("bin")) + (consumer == "BuildProductNoSeparator" ? "" : Path.DirectorySeparatorChar.ToString()))))
            : consumer == "Cache"
            ? new XElement("Target", new XAttribute("Name", "_RoslynMcpForkAuthoredCacheHook"),
                new XAttribute("BeforeTargets", target),
                new XElement("PropertyGroup", new XElement("ProjectAssetsCacheFile", protectedOutput)))
            : new XElement("Target", new XAttribute("Name", "_RoslynMcpForkAuthoredCopyHook"),
                new XAttribute("BeforeTargets", target),
                new XElement("ItemGroup", new XElement(itemName, new XAttribute("Update", "input.txt"),
                    new XAttribute(consumer == "CopyLocal" ? "DestinationSubDirectory" : "TargetPath",
                        consumer == "CopyLocal" ? sourceOutput ? "../../" : "safe/" : consumer == "BackslashAlways" ? sourceOutput ? "..\\..\\protected.txt" : "safe\\nested.txt" : sourceOutput ? "../../protected.txt" : "safe.txt"))));
        var initial = consumer is "Cache" or "BuildProduct" or "BuildProductNoSeparator" ? null : new XElement("ItemGroup",
            new XElement(itemName, new XAttribute("Include", "input.txt"),
                new XAttribute(consumer == "CopyLocal" ? "DestinationSubDirectory" : "TargetPath",
                    consumer == "CopyLocal" ? "safe/" : "safe.txt")));
        fixture.WriteProject(project, initial?.ToString() ?? string.Empty);
        body.Add(new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(fixture.Evidence, "hook.txt")),
            new XAttribute("Lines", "hook")));
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project", body).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        if (guarded) ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var configuration = guarded ? ForkAdmissionMaterial.Find(project) : null;
        using var execution = configuration is null ? null : await ForkAdmissionExecution.CreateAsync(configuration,
            DateTimeOffset.UtcNow.AddMinutes(1), timeout.Token);
        var arguments = consumer.StartsWith("BuildProduct", StringComparison.Ordinal)
            ? new[] { "build", project, "--no-restore", "--nologo", "-nr:false", "-p:UseSharedCompilation=false" }
            : new[] { "msbuild", project, "-nologo", "-nr:false", "-t:PrepareForBuild;" + target };
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project,
            execution?.Arguments(arguments) ?? arguments.ToArray(), timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(fixture.Evidence, "hook.txt")).Length, "The effective late imported hook must run once.");
        if (guarded && sourceOutput)
        {
            Assert.IsFalse(result.Succeeded, "The actual native consumer must refuse.");
            Assert.IsFalse(File.Exists(protectedOutput), "Native path admission must run after the authored mutator and before its write.");
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsTrue(File.Exists(protectedOutput), "The ordinary SDK task must independently demonstrate its source write.");
            if (consumer == "Cache") Assert.IsTrue(new FileInfo(protectedOutput).Length > 0, "Default cache writing must remain enabled.");
            else if (consumer.StartsWith("BuildProduct", StringComparison.Ordinal)) CollectionAssert.AreEqual(File.ReadAllBytes(fixture.PathInFork("obj/Debug/net10.0/Consumer.dll")), File.ReadAllBytes(protectedOutput));
            else Assert.AreEqual("native consumer bytes", File.ReadAllText(protectedOutput));
        }
    }


    [TestMethod]
    [DataRow("late-safe", true, false)]
    [DataRow("marker", false, true)]
    [DataRow("marker", true, true)]
    [DataRow("marker", true, false)]
    [DataRow("marker-disabled", true, true)]
    [DataRow("marker-prior-flags", false, true)]
    [DataRow("marker-prior-flags", true, true)]
    [DataRow("marker-prior-flags", true, false)]
    [DataRow("application", false, true)]
    [DataRow("application", true, true)]
    [DataRow("application", true, false)]
    [DataRow("deploy", false, true)]
    [DataRow("deploy", true, true)]
    [DataRow("deploy", true, false)]
    [DataRow("manifest-disabled", true, true)]
    public async Task NativeLateConsumers_CopyMarkerAndManifests_RespectActualHooksAndConditions(string consumer, bool guarded, bool sourceOutput)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Consumer.csproj");
        File.WriteAllText(fixture.PathInFork("input.txt"), "native late consumer bytes");
        var marker = consumer.StartsWith("marker", StringComparison.Ordinal);
        var lateSafe = consumer == "late-safe";
        var target = lateSafe ? "_CopyOutOfDateSourceItemsToOutputDirectoryAlways" : marker ? "_CopyFilesMarkedCopyLocal" : "_CopyManifestFiles";
        var output = lateSafe ? fixture.PathInFork("bin/safe.txt") : marker ? fixture.ProtectedOutput : Path.Combine(fixture.Source, "input.txt");
        if (!sourceOutput && !lateSafe) output = marker ? fixture.PathInFork("allowed/marker") : fixture.PathInFork("allowed/input.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var initial = marker ? "<ItemGroup><ReferenceCopyLocalPaths Include=\"input.txt\" /></ItemGroup>"
            : lateSafe ? "<ItemGroup><Content Include=\"input.txt\" CopyToOutputDirectory=\"Always\" TargetPath=\"../../protected.txt\" /><_SourceItemsToCopyToOutputDirectoryAlways Include=\"input.txt\" TargetPath=\"../../protected.txt\" /></ItemGroup>"
            : "<PropertyGroup><GenerateClickOnceManifests>true</GenerateClickOnceManifests></PropertyGroup>";
        fixture.WriteProject(project, initial);
        var hook = new XElement("Target", new XAttribute("Name", "ActualLateConsumerMutation"), new XAttribute("BeforeTargets", target));
        if (lateSafe)
            hook.Add(new XElement("ItemGroup", new XElement("_SourceItemsToCopyToOutputDirectoryAlways", new XAttribute("Update", "input.txt"), new XAttribute("TargetPath", "safe.txt"))));
        else if (marker)
        {
            hook.Add(new XElement("ItemGroup", new XElement("CopyUpToDateMarker", new XAttribute("Remove", "@(CopyUpToDateMarker)")),
                new XElement("CopyUpToDateMarker", new XAttribute("Include", output))));
            if (consumer == "marker-prior-flags")
            {
                hook.Add(new XElement("PropertyGroup", new XElement("UseCommonOutputDirectory", "true"), new XElement("WroteAtLeastOneFile", "true")));
                hook.Add(new XElement("ItemGroup", new XElement("ReferencesCopiedInThisBuild", new XAttribute("Include", "input.txt"))));
            }
            if (consumer == "marker-disabled") hook.Add(new XElement("PropertyGroup", new XElement("UseCommonOutputDirectory", "true")));
        }
        else
        {
            hook.Add(new XElement("PropertyGroup", new XElement("OutDir", Path.GetDirectoryName(output)),
                new XElement("GenerateClickOnceManifests", consumer == "deploy" ? "true" : "false"),
                new XElement("_DeploymentCopyApplicationManifest", consumer == "application" ? "true" : "false")));
            hook.Add(new XElement("ItemGroup",
                new XElement("ApplicationManifest", new XAttribute("Remove", "@(ApplicationManifest)")),
                new XElement("DeployManifest", new XAttribute("Remove", "@(DeployManifest)")),
                new XElement(consumer == "deploy" ? "DeployManifest" : "ApplicationManifest", new XAttribute("Include", "input.txt"))));
        }
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project", hook).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        var config = guarded ? ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token) : null;
        using var execution = config is null ? null : await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(1), timeout.Token);
        string[] arguments = ["msbuild", project, "-nologo", "-nr:false", "-t:PrepareForBuild;" + target];
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project, execution?.Arguments(arguments) ?? arguments, timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        if (consumer.EndsWith("-disabled", StringComparison.Ordinal))
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsFalse(File.Exists(output), "A native disabled branch must not produce an admission refusal or effect.");
        }
        else if (guarded && sourceOutput)
        {
            Assert.IsFalse(result.Succeeded, "Admission must refuse before the native marker/manifest write.");
            Assert.IsFalse(File.Exists(output));
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsTrue(File.Exists(output), "The real native consumer must independently produce its output.");
            if (!marker) Assert.AreEqual("native late consumer bytes", File.ReadAllText(output));
        }
    }

    [TestMethod]
    public void OwnedProjectImports_PreserveUtf16CommentAndMetadata_WhenInstalledAndStripped()
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Unicode.custombuild");
        var guard = fixture.PathInFork("guard.targets");
        File.WriteAllText(guard, "<Project />");
        File.WriteAllText(project, "<Project><!-- Café Ω --><ItemGroup><None Include=\"input.txt\" Note=\"漢字\" /></ItemGroup></Project>",
            System.Text.Encoding.Unicode);
        var previous = new ForkAdmissionMaterial.Configuration(1, fixture.Source, fixture.Fork, guard, string.Empty);
        ForkProjectImports.Install(project, fixture.Fork, guard, () => { });
        Assert.AreEqual(System.Text.Encoding.Unicode.CodePage, SourceFileEncoding.FromBytes(File.ReadAllBytes(project)).CodePage);
        StringAssert.Contains(File.ReadAllText(project), "<!-- Café Ω -->");
        Assert.AreEqual("漢字", (string?)XDocument.Load(project).Descendants("None").Single().Attribute("Note"));
        ForkAdmissionMaterial.StripPreviousImports(fixture.Fork, previous, CancellationToken.None);
        Assert.AreEqual(System.Text.Encoding.Unicode.CodePage, SourceFileEncoding.FromBytes(File.ReadAllBytes(project)).CodePage);
        Assert.AreEqual(0, XDocument.Load(project).Descendants("Import").Count());
        StringAssert.Contains(File.ReadAllText(project), "<!-- Café Ω -->");
        Assert.AreEqual("漢字", (string?)XDocument.Load(project).Descendants("None").Single().Attribute("Note"));
    }


    [TestMethod]
    public async Task NativeConcurrentParents_InstallBeforeEachFirstChildMetadataEvaluation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var child = fixture.PathInFork("Child.custombuild");
        fixture.WriteProject(child, """
            <PropertyGroup><LanguageTargets>$(MSBuildToolsPath)/Microsoft.CSharp.targets</LanguageTargets></PropertyGroup>
            <Target Name="FirstMetadata" BeforeTargets="GetTargetFrameworks">
              <WriteLinesToFile File="$(ProofEvidence)/metadata-$(Flavor).txt" Lines="$(RoslynMcpForkConfig)" Overwrite="true" />
            </Target>
            """);
        var parents = new[] { fixture.PathInFork("ParentA.csproj"), fixture.PathInFork("ParentB.csproj") };
        foreach (var parent in parents)
            fixture.WriteProject(parent, """
                <Target Name="LateReference" BeforeTargets="_GetProjectReferenceTargetFrameworkProperties">
                  <ItemGroup>
                    <_MSBuildProjectReferenceExistent Include="Child.custombuild" />
                    <ProjectReferenceWithConfiguration Include="Child.custombuild" BuildReference="true" ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Target>
                """);
        var root = fixture.PathInFork("Root.proj");
        File.WriteAllText(root, """
            <Project>
              <ItemGroup><Parent Include="ParentA.csproj" AdditionalProperties="Flavor=A" /><Parent Include="ParentB.csproj" AdditionalProperties="Flavor=B" /></ItemGroup>
              <Target Name="Query"><MSBuild Projects="@(Parent)" Targets="PrepareProjectReferences" BuildInParallel="true" /></Target>
            </Project>
            """);
        await fixture.RestoreAsync(child, DotnetCommandRunner, timeout.Token);
        foreach (var parent in parents) await fixture.RestoreAsync(parent, DotnetCommandRunner, timeout.Token);
        var configuration = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, parents, timeout.Token);
        Assert.IsFalse(File.ReadAllText(child).Contains(ForkProjectImports.Label, StringComparison.Ordinal));
        using var execution = await ForkAdmissionExecution.CreateAsync(configuration, DateTimeOffset.UtcNow.AddMinutes(1), timeout.Token);
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, root,
            execution.Arguments(["msbuild", root, "-t:Query", "-m:2", "-nr:false", "-nologo", "-p:ProofEvidence=" + fixture.Evidence]), timeout.Token);
        execution.Complete();
        Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
        execution.ThrowIfRefused();
        foreach (var flavor in new[] { "A", "B" })
            StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.Evidence, "metadata-" + flavor + ".txt")), configuration.GuardTargetsPath.Replace("guard.targets", "config.json"));
        Assert.AreEqual(1, XDocument.Load(child).Root!.Elements("Import").Count(element =>
            (string?)element.Attribute("Label") == ForkProjectImports.Label));
    }

    [TestMethod]
    public async Task NativeCrossTargeting_InnerQueriesAndDispatch_PreserveGuardedContexts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Multi.csproj");
        fixture.WriteProject(project, """
            <PropertyGroup><TargetFramework /><TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks></PropertyGroup>
            <Target Name="ObserveInnerQuery" BeforeTargets="GetTargetFrameworksWithPlatformForSingleTargetFramework" Condition="'$(TargetFramework)' != ''">
              <WriteLinesToFile File="$(ProofEvidence)/query-$(TargetFramework).txt" Lines="$(RoslynMcpForkConfig)" Overwrite="true" />
            </Target>
            <Target Name="InnerConsumer" DependsOnTargets="PrepareForBuild">
              <WriteLinesToFile File="$(ProofEvidence)/dispatch-$(TargetFramework).txt" Lines="$(RoslynMcpForkConfig)" Overwrite="true" />
            </Target>
            """);
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        var configuration = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        using var execution = await ForkAdmissionExecution.CreateAsync(configuration, DateTimeOffset.UtcNow.AddMinutes(1), timeout.Token);
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project,
            execution.Arguments(["msbuild", project, "-t:GetTargetFrameworksWithPlatformFromInnerBuilds;DispatchToInnerBuilds",
                "-p:InnerTargets=InnerConsumer", "-p:ProofEvidence=" + fixture.Evidence, "-m:2", "-nr:false", "-nologo"]), timeout.Token);
        execution.Complete();
        Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
        execution.ThrowIfRefused();
        foreach (var framework in new[] { "net10.0", "netstandard2.1" })
            foreach (var boundary in new[] { "query", "dispatch" })
                StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.Evidence, boundary + "-" + framework + ".txt")),
                    configuration.GuardTargetsPath.Replace("guard.targets", "config.json"));
    }


    [TestMethod]
    [DataRow("default", false, true)]
    [DataRow("default", true, true)]
    [DataRow("private", false, true)]
    [DataRow("private", true, true)]
    [DataRow("nontransitive", false, true)]
    [DataRow("nontransitive", true, true)]
    [DataRow("default", true, false)]
    [DataRow("private", true, false)]
    [DataRow("nontransitive", true, false)]
    public async Task NativeReferenceGraph_SelectedFrameworkAndLocalCopies_AreAdmitted(string mode, bool guarded, bool sourceOutput)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var fixture = new NativeFixture(false);
        File.WriteAllText(fixture.PathInFork("Directory.Build.props"), """
            <Project><PropertyGroup><BaseIntermediateOutputPath>obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath><DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences></PropertyGroup></Project>
            """);
        var childDirectory = fixture.PathInFork("a/b/c");
        Directory.CreateDirectory(childDirectory);
        var parent = fixture.PathInFork("Parent.csproj");
        var bridge = fixture.PathInFork("Bridge.csproj");
        var child = Path.Combine(childDirectory, "Child.csproj");
        var leaf = Path.Combine(childDirectory, "Leaf.csproj");
        fixture.WriteProject(parent, """
            <ItemGroup><ProjectReference Include="Bridge.csproj" AdditionalProperties="RuntimeIdentifier=win-x64;SelfContained=true;Flavor=inherited" /></ItemGroup>
            <ItemGroup><Content Include="parent.txt" CopyToOutputDirectory="Always" TargetPath="parent.txt" /></ItemGroup>
            """);
        fixture.WriteProject(bridge, """
            <PropertyGroup><RuntimeIdentifiers>win-x64</RuntimeIdentifiers><IsRidAgnostic>false</IsRidAgnostic></PropertyGroup>
            """ + new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "a/b/c/Child.csproj"),
                new XAttribute("Private", mode == "private" ? "false" : "true"))) + """
            <ItemGroup><Content Include="bridge.txt" CopyToOutputDirectory="Always" TargetPath="bridge.txt" /></ItemGroup>
            """);
        fixture.WriteProject(child, """
            <PropertyGroup><TargetFramework /><TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>
              <TargetFrameworks Condition="'$(RuntimeIdentifier)' != ''">netstandard2.1</TargetFrameworks><IsRidAgnostic>true</IsRidAgnostic></PropertyGroup>
            <ItemGroup><ProjectReference Include="Leaf.csproj" /></ItemGroup>
            """ + new XElement("ItemGroup", new XElement("Content", new XAttribute("Include", "child.txt"),
                new XAttribute("CopyToOutputDirectory", "Always"),
                new XAttribute("TargetPath", sourceOutput && mode == "default" ? "../../protected.txt" : "child.txt"))));
        fixture.WriteProject(leaf, """
            <PropertyGroup><TargetFramework /><TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks></PropertyGroup>
            <ItemGroup><Content Include="leaf.txt" CopyToOutputDirectory="Always" TargetPath="leaf.txt" /></ItemGroup>
            """);
        foreach (var (path, text) in new[] { (fixture.PathInFork("parent.txt"), "parent"), (fixture.PathInFork("bridge.txt"), "bridge"),
            (Path.Combine(childDirectory, "child.txt"), "child"), (Path.Combine(childDirectory, "leaf.txt"), "leaf") })
            File.WriteAllText(path, text);
        var targets = new XElement("Project",
            new XElement("Target", new XAttribute("Name", "ObserveNativeBuildContext"), new XAttribute("BeforeTargets", "PrepareForBuild"),
                new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(fixture.Evidence, "context-$(MSBuildProjectName).txt")),
                    new XAttribute("Lines", "$(TargetFramework)|$(RuntimeIdentifier)|$(SelfContained)|$(Flavor)"), new XAttribute("Overwrite", "true"))),
            new XElement("Target", new XAttribute("Name", "ObserveFullItems"), new XAttribute("AfterTargets", "GetCopyToOutputDirectoryItems"),
                new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(fixture.Evidence, "full-$(MSBuildProjectName).txt")),
                    new XAttribute("Lines", "@(AllItemsFullPathWithTargetPath->'%(TargetPath)')"), new XAttribute("Overwrite", "true"))),
            new XElement("Target", new XAttribute("Name", "ObserveLocalItems"), new XAttribute("AfterTargets", "_GetCopyToOutputDirectoryItemsFromThisProject"),
                new XElement("WriteLinesToFile", new XAttribute("File", Path.Combine(fixture.Evidence, "this-$(MSBuildProjectName).txt")),
                    new XAttribute("Lines", "@(_ThisProjectItemsToCopyToOutputDirectory->'%(TargetPath)')"), new XAttribute("Overwrite", "true"))));
        if (sourceOutput && mode != "default")
            targets.Add(new XElement("Target", new XAttribute("Name", "MutateLocalChildCopy"),
                new XAttribute("BeforeTargets", "_CopyOutOfDateSourceItemsToOutputDirectoryAlways"),
                new XAttribute("Condition", "'$(MSBuildProjectName)' == 'Child'"),
                new XElement("ItemGroup", new XElement("_SourceItemsToCopyToOutputDirectoryAlways", new XAttribute("Update", Path.Combine(childDirectory, "child.txt")),
                    new XAttribute("TargetPath", "../../../../../child.txt")))));
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), targets.ToString());
        await fixture.RestoreAsync(parent, DotnetCommandRunner, timeout.Token);
        var configuration = guarded ? ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [parent, bridge, child, leaf], timeout.Token) : null;
        using var execution = configuration is null ? null : await ForkAdmissionExecution.CreateAsync(configuration, DateTimeOffset.UtcNow.AddMinutes(2), timeout.Token);
        var arguments = new List<string> { "build", parent, "--no-restore", "--nologo", "-nr:false", "-p:UseSharedCompilation=false" };
        if (mode == "nontransitive") arguments.Add("-p:MSBuildCopyContentTransitively=false");
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, parent, execution?.Arguments(arguments) ?? arguments.ToArray(), timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        Assert.AreEqual("net10.0|||inherited", File.ReadAllText(Path.Combine(fixture.Evidence, "context-Child.txt")).Trim(),
            "Native framework selection must remove RID and SelfContained from the actual child build.");
        StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.Evidence, "context-Bridge.txt")), "net10.0|win-x64|true|inherited");
        var protectedOutput = mode == "default" ? fixture.ProtectedOutput : Path.Combine(fixture.Source, "child.txt");
        if (guarded && sourceOutput)
        {
            Assert.IsFalse(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsFalse(File.Exists(protectedOutput));
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            if (sourceOutput) Assert.AreEqual("child", File.ReadAllText(protectedOutput));
            else Assert.AreEqual("child", File.ReadAllText(Path.Combine(childDirectory, "bin", "child.txt")));
            var parentItems = File.ReadAllLines(Path.Combine(fixture.Evidence, "full-Parent.txt"));
            Assert.AreEqual(mode == "default" ? 4 : 2, parentItems.Length, "Full membership must remain the SDK's transitive/private selection.");
            Assert.AreEqual(1, File.ReadAllLines(Path.Combine(fixture.Evidence, "this-Child.txt")).Length);
        }
    }

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
        using var execution = config is null ? null : await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(1), timeout.Token);
        string[] arguments = ["build", project, "--no-restore", "--nologo", "-nr:false", "-p:UseSharedCompilation=false"];
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project, execution?.Arguments(arguments) ?? arguments, timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        if (branch == "disabled")
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsFalse(File.Exists(output), "Native disabled branches must not cause false admission refusals or copies.");
        }
        else if (guarded && sourceOutput)
        {
            Assert.IsFalse(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsFalse(File.Exists(output));
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
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
        Assert.IsTrue(ordinary.Succeeded, ordinary.StdOut + ordinary.StdErr);
        var product = Directory.GetFiles(destination, "probe.cs", SearchOption.AllDirectories).Single();
        var bytes = File.ReadAllBytes(product);
        Assert.AreEqual("// native NativeMatrix", File.ReadAllText(product));
        var membership = string.Join("\n", File.ReadAllLines(Path.Combine(fixture.Evidence, "membership.txt")));
        if (!copy && action == "None") Assert.AreEqual(string.Empty, membership, "Native FileWrites-only products must not be inferred from SDK copy/processed/FileWrites arrays.");
        File.Delete(product);
        File.Delete(Path.Combine(fixture.Evidence, "hooks.txt"));
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        using var execution = await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(1), timeout.Token);
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
            Assert.IsTrue(candidate.Succeeded, candidate.StdOut + candidate.StdErr);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(product));
            Assert.AreEqual(membership, string.Join("\n", File.ReadAllLines(Path.Combine(fixture.Evidence, "membership.txt"))));
        }
    }

    [TestMethod]
    public void AdmissionInstallation_PreservesUnreferencedProjectShapedData()
    {
        using var fixture = new NativeFixture(false);
        var entry = fixture.PathInFork("Entry.custombuild");
        fixture.WriteProject(entry, string.Empty);
        var data = fixture.PathInFork("fixtures/Unloaded.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(data)!);
        File.WriteAllText(data, "<Project><!-- Fixture bytes: Café Ω --><PropertyGroup><Example>漢字</Example></PropertyGroup></Project>", System.Text.Encoding.Unicode);
        var before = File.ReadAllBytes(data);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [entry], CancellationToken.None);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(data), "Unloaded project-shaped test data must remain byte-identical.");
        Assert.AreEqual(1, XDocument.Load(entry).Root!.Elements("Import").Count(import => (string?)import.Attribute("Label") == ForkProjectImports.Label));
    }

    [TestMethod]
    public async Task ExpiredForkSweep_PreservesActiveAndUncertainExecution_ThenDeletesCompletedMaterial()
    {
        using var fixture = new NativeFixture(false);
        var forks = Path.Combine(fixture.Root, "forks");
        var fork = Path.Combine(forks, "20000101T000000000Z-native");
        Directory.CreateDirectory(fork);
        var project = Path.Combine(fork, "Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fork, [project], CancellationToken.None);
        using var execution = await ForkAdmissionExecution.CreateAsync(config, null, CancellationToken.None);
        WorkspaceForkApplyService.SweepExpiredForks(forks, 1, DateTimeOffset.UtcNow);
        Assert.IsTrue(Directory.Exists(execution.Root), "TTL cleanup must preserve admission material during an active invocation.");
        execution.Dispose();
        Assert.IsNotNull(execution.CleanupWarning);
        WorkspaceForkApplyService.SweepExpiredForks(forks, 1, DateTimeOffset.UtcNow);
        Assert.IsTrue(Directory.Exists(execution.Root), "Uncertain native completion retains its execution and blocks expiry.");
        // This test never starts native work, so it can remove the uncertain residue.
        Directory.Delete(execution.Root, recursive: true);
        using var completed = await ForkAdmissionExecution.CreateAsync(config, null, CancellationToken.None);
        completed.Complete();
        completed.Dispose();
        WorkspaceForkApplyService.SweepExpiredForks(forks, 1, DateTimeOffset.UtcNow);
        Assert.IsFalse(Directory.Exists(fork), "Completed expired forks must still be collected.");
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-guard")]
    [DataRow("malformed-owned")]
    public async Task RetainedFork_ProjectFilePreviewReplay_StripsOnlyPreviousOwnedImport(string material)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var project = Path.Combine(fixture.Source, "Entry.csproj");
        fixture.WriteProject(project, "<Import Project=\"Authored.targets\" Label=\"RoslynMcpForkAdmission\" />");
        File.WriteAllText(Path.Combine(fixture.Source, "Authored.targets"), "<Project><!-- Café Ω authored import --></Project>");
        var solutionPath = Path.Combine(fixture.Source, "Entry.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"Entry.csproj\" /></Solution>");
        var restored = await DotnetCommandRunner.RunAsync(fixture.Source, project, ["restore", project, "--nologo", "-nr:false"], timeout.Token);
        Assert.IsTrue(restored.Succeeded, restored.StdOut + restored.StdErr);
        var source = await WorkspaceManager.LoadAsync(solutionPath, timeout.Token);
        var store = new PreviewStore();
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
            TestRunnerService, WorkspaceManager, ChangeTracker);
        var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, validation,
            TestRunnerService, DotnetCommandRunner, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance);
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? first = null;
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? second = null;
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var token = store.Store(source.WorkspaceId, original, original, WorkspaceManager.GetCurrentVersion(source.WorkspaceId),
                "first retained fork", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
            first = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "first", timeout.Token);
            Assert.IsTrue(first.Success);
            Assert.IsNotNull(first.ForkWorkspaceId);
            var firstProject = Path.Combine(first.ForkPath, "Entry.csproj");
            var oldConfig = ForkAdmissionMaterial.Find(firstProject)!;
            var guard = XDocument.Load(oldConfig.GuardTargetsPath);
            guard.Root!.Add(new XElement("Target", new XAttribute("Name", "PreviousGuardMustNeverRunInCopiedProject"),
                new XAttribute("BeforeTargets", "PrepareForBuild"), new XAttribute("Condition", "'$(MSBuildProjectDirectory)' != '" + first.ForkPath + "'"),
                new XElement("Error", new XAttribute("Text", "PREVIOUS_GUARD_EXECUTED_IN_NEW_FORK"))));
            guard.Save(oldConfig.GuardTargetsPath);
            var firstSolution = WorkspaceManager.GetCurrentSolution(first.ForkWorkspaceId);
            var firstVersion = WorkspaceManager.GetCurrentVersion(first.ForkWorkspaceId);
            var firstStatus = await WorkspaceManager.GetStatusAsync(first.ForkWorkspaceId);
            var projectText = File.ReadAllText(firstProject).Replace("</Project>", "<!-- Replayed Café 漢字 --></Project>", StringComparison.Ordinal);
            if (material == "malformed-owned")
            {
                var replay = XDocument.Parse(projectText, LoadOptions.PreserveWhitespace);
                replay.Root!.Elements("Import").Single(import => (string?)import.Attribute("Project") == oldConfig.GuardTargetsPath)
                    .Add(new XAttribute("Condition", "'1' == '1'"));
                projectText = replay.ToString(SaveOptions.DisableFormatting);
            }
            if (material == "missing-guard") File.Delete(oldConfig.GuardTargetsPath);
            var modified = firstSolution.AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(firstSolution.ProjectIds.Single()),
                "project-file-replay", Microsoft.CodeAnalysis.Text.SourceText.From(projectText, System.Text.Encoding.Unicode), filePath: firstProject);
            var secondToken = store.Store(first.ForkWorkspaceId, firstSolution, modified, firstVersion,
                "project XML preview reintroduces previous owned absolute import", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
            var entry = WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, secondToken);
            var bytes = Directory.GetFiles(first.ForkPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            if (material != "valid")
            {
                var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    service.ApplyAsync(first.ForkWorkspaceId, secondToken, "keep", false, null, "second", timeout.Token));
                StringAssert.Contains(refusal.Message, "Recreate the fork");
            }
            else
            {
                second = await service.ApplyAsync(first.ForkWorkspaceId, secondToken, "keep", false, null, "second", timeout.Token);
                Assert.IsTrue(second.Success);
                Assert.IsNotNull(second.ForkWorkspaceId);
                var secondProject = Path.Combine(second.ForkPath, "Entry.csproj");
                var nextConfig = ForkAdmissionMaterial.Find(secondProject)!;
                Assert.AreEqual(first.ForkPath, nextConfig.SourceRoot, "Admission must use the actual retained source/current fork pair.");
                var xml = XDocument.Load(secondProject);
                Assert.AreEqual(0, xml.Root!.Elements("Import").Count(import => (string?)import.Attribute("Project") == oldConfig.GuardTargetsPath));
                Assert.AreEqual(1, xml.Root.Elements("Import").Count(import => (string?)import.Attribute("Project") == nextConfig.GuardTargetsPath));
                Assert.AreEqual(1, xml.Root.Elements("Import").Count(import => (string?)import.Attribute("Project") == "Authored.targets"));
                StringAssert.Contains(File.ReadAllText(secondProject), "Replayed Café 漢字");
                using var reader = new StreamReader(secondProject, detectEncodingFromByteOrderMarks: true);
                reader.Peek();
                Assert.AreEqual(1200, reader.CurrentEncoding.CodePage);
            }
            Assert.AreSame(firstSolution, WorkspaceManager.GetCurrentSolution(first.ForkWorkspaceId));
            Assert.AreEqual(firstVersion, WorkspaceManager.GetCurrentVersion(first.ForkWorkspaceId));
            Assert.AreEqual(firstStatus.SnapshotToken, (await WorkspaceManager.GetStatusAsync(first.ForkWorkspaceId)).SnapshotToken);
            Assert.AreEqual(entry, WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, secondToken));
            foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
        }
        finally
        {
            if (second?.ForkWorkspaceId is { } secondId) WorkspaceManager.Close(secondId);
            if (first?.ForkWorkspaceId is { } firstId) WorkspaceManager.Close(firstId);
            WorkspaceManager.Close(source.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedInvocation_RealVstestAndNativeMtp_AcceptForwardedAdmissionProperties(bool nativeMtp)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Probe.csproj");
        var body = new XElement("PropertyGroup", new XElement("IsTestProject", "true"),
            new XElement("EnableMSTestRunner", nativeMtp ? "true" : "false"), new XElement("OutputType", nativeMtp ? "Exe" : "Library")) +
            new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Probe.cs")),
                new XElement("PackageReference", new XAttribute("Include", "Microsoft.NET.Test.Sdk"), new XAttribute("Version", "18.11.0")),
                new XElement("PackageReference", new XAttribute("Include", "MSTest.TestAdapter"), new XAttribute("Version", "4.5.1")),
                new XElement("PackageReference", new XAttribute("Include", "MSTest.TestFramework"), new XAttribute("Version", "4.5.1"))).ToString();
        if (nativeMtp)
        {
            body += new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Microsoft.Testing.Extensions.TrxReport"), new XAttribute("Version", "2.5.1")));
            File.WriteAllText(fixture.PathInFork("global.json"), "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}");
        }
        fixture.WriteProject(project, body);
        File.WriteAllText(fixture.PathInFork("Probe.cs"), "using Microsoft.VisualStudio.TestTools.UnitTesting; [TestClass] public sealed class NativeProbe { [TestMethod] public void Probe() { Assert.AreEqual(2, 1 + 1); } }");
        var marker = Path.Combine(fixture.Evidence, "forwarded.txt");
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "ObserveActualInvocationProperties"), new XAttribute("BeforeTargets", "PrepareForBuild"),
                new XElement("WriteLinesToFile", new XAttribute("File", marker), new XAttribute("Lines", "$(RoslynMcpForkExecutionRoot)"), new XAttribute("Overwrite", "true")))).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        try
        {
            File.Delete(marker);
            var results = Path.Combine(fixture.Evidence, "results");
            string[] arguments = nativeMtp
                ? ["test", "--project", project, "--report-trx", "--results-directory", results, "--no-restore"]
                : ["test", project, "--no-restore", "--nologo", "--logger", "trx", "--results-directory", results];
            var result = await GatedCommandExecutor.ExecuteAsync(loaded.WorkspaceId, project, arguments, TimeSpan.FromMinutes(2), timeout.Token);
            TestContext.WriteLine(result.StdOut + result.StdErr);
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            var trx = XDocument.Load(Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories).Single());
            var counters = trx.Descendants().Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("1", (string?)counters.Attribute("total"));
            Assert.AreEqual("1", (string?)counters.Attribute("passed"));
            StringAssert.StartsWith(File.ReadAllText(marker).Trim(), Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions") + Path.DirectorySeparatorChar);
            Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions")).Length);
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("cancel")]
    [DataRow("deadline")]
    [DataRow("stale")]
    [DataRow("construction")]
    [DataRow("pre-cancel")]
    public async Task AdmissionExecution_OwnershipWaitAndConstruction_ReleaseReferencesWithoutResurrection(string mode)
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        if (mode is "cancel" or "deadline")
        {
            using var blocked = await WorkspaceForkApplyService.AcquireForkApplyLockAsync(fixture.Fork, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var pending = ForkAdmissionExecution.CreateAsync(config, mode == "deadline" ? DateTimeOffset.UtcNow.AddMilliseconds(100) : null, cancellation.Token).AsTask();
            Assert.IsFalse(pending.IsCompleted, "The same existing per-fork critical section must serialize invocation start and deletion.");
            Assert.IsNull(WorkspaceForkApplyService.TryAcquireForkApplyLock(fixture.Fork));
            if (mode == "cancel")
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            }
            else await Assert.ThrowsExactlyAsync<TimeoutException>(() => pending);
        }
        else if (mode == "pre-cancel")
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(-1), cancellation.Token).AsTask());
        }
        else if (mode == "stale")
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Fork);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ForkAdmissionExecution.CreateAsync(config, null, CancellationToken.None).AsTask());
            Assert.IsFalse(Directory.Exists(fixture.Fork), "A Find snapshot must not resurrect material deleted before ownership acquisition.");
        }
        else
        {
            var executions = Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions");
            File.WriteAllText(executions, "Block directory creation before any native work starts.");
            await Assert.ThrowsAsync<IOException>(() => ForkAdmissionExecution.CreateAsync(config, null, CancellationToken.None).AsTask());
            Assert.IsFalse(Directory.Exists(executions));
        }
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork), "All failed/wait-cancelled construction paths must release their ownership reference.");
    }

    [TestMethod]
    public void ForkCleanup_OwnershipResolutionFailure_RetainsMaterialAndPreservesPrimaryCancellation()
    {
        using var fixture = new NativeFixture(false);
        var cycle = Path.Combine(fixture.Root, "cycle");
        WorkspaceForkGraphReplayTests.CreateLink(cycle, cycle);
        try
        {
            Assert.ThrowsExactly<IOException>(() => PhysicalPathResolver.Resolve(cycle));
            var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, new PreviewStore(),
                null!, null!, DotnetCommandRunner, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance);
            var cleanup = typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var warnings = new List<string>();
            var original = new OperationCanceledException("Primary cancellation must survive cleanup identity failure.");
            var observed = Assert.ThrowsExactly<OperationCanceledException>(() =>
            {
                try { throw original; }
                catch
                {
                    cleanup.Invoke(service, [null, Path.Combine(cycle, "fork"), warnings, CancellationToken.None]);
                    throw;
                }
            });
            Assert.AreSame(original, observed);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings.Single(), "completion could not be verified");
            Assert.IsNotNull(new DirectoryInfo(cycle).LinkTarget, "Cleanup must retain unverifiable material.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(cycle); }
    }

    [TestMethod]
    public async Task ForkCleanup_ActiveInvocationRetainsWorkspaceAndMaterial_CompletedInvocationDeletesBoth()
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, CancellationToken.None);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        var loaded = await WorkspaceManager.LoadAsync(project, CancellationToken.None);
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
            using var execution = await ForkAdmissionExecution.CreateAsync(config, null, CancellationToken.None);
            var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, new PreviewStore(),
                null!, null!, DotnetCommandRunner, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance);
            var cleanup = typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var warnings = new List<string>();
            cleanup.Invoke(service, [loaded.WorkspaceId, fixture.Fork, warnings, CancellationToken.None]);
            Assert.AreEqual(1, warnings.Count);
            Assert.IsTrue(Directory.Exists(execution.Root));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId), "A refused cleanup must not close the active invocation's workspace.");
            execution.Complete();
            execution.Dispose();
            warnings.Clear();
            cleanup.Invoke(service, [loaded.WorkspaceId, fixture.Fork, warnings, CancellationToken.None]);
            Assert.AreEqual(0, warnings.Count);
            Assert.IsFalse(Directory.Exists(fixture.Fork));
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("activation")]
    [DataRow("native-error")]
    [DataRow("native-error-pruned")]
    [DataRow("native-error-sourcelink")]
    [DataRow("native-error-vs")]
    public async Task NativeBuildHost_TaskFailureRefusesEvenWhenDiagnosticQueueEvictsEverything(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("pruned/Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var guard = XDocument.Load(config.GuardTargetsPath);
        if (failure == "activation")
            guard.Descendants("RoslynMcpForkTaskAssembly").Single().Value = Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "tasks", "RoslynMcp.Core.dll");
        else
        {
            var message = failure switch
            {
                "native-error-pruned" => "Native build failed loading a pruned task assembly.",
                "native-error-sourcelink" => "Task failed: Unable to locate repository with working directory that contains directory 'ordinary'.",
                "native-error-vs" => "ResolveComReference: Cannot register type library 'TLB1234' for COM interop.",
                _ => "Intentional native failure before BuildHost can return a partial clean solution."
            };
            guard.Root!.Add(new XElement("Target", new XAttribute("Name", "NativeFailureBeforeCleanPartialLoad"), new XAttribute("BeforeTargets", "PrepareForBuild"),
                new XElement("Error", new XAttribute("Text", message))));
        }
        guard.Save(config.GuardTargetsPath);
        var diagnostics = new WorkspaceDiagnosticsSink(0);
        var loader = new WorkspaceSessionLoader();
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => loader.CreateAndOpenAsync(Guid.NewGuid().ToString("N"), project, null,
            diagnostics, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, timeout.Token));
        Assert.AreEqual(0, diagnostics.Queue.Count, "Refusal must survive eviction of every native diagnostic.");
        Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions")).Length);
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
    }

    [TestMethod]
    [DataRow("marker")]
    [DataRow("cleanup")]
    public async Task AdmissionExecution_CancellationMarkerAndCleanupFailures_PreservePrimaryCancellationAndReleaseOwnership(string failure)
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        using var execution = await ForkAdmissionExecution.CreateAsync(config, null, cancellation.Token);
        FileStream? held = null;
        try
        {
            if (failure == "marker") Directory.CreateDirectory(Path.Combine(execution.Root, "cancelled"));
            else
            {
                if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing supplies this deterministic cleanup refusal.");
                held = new FileStream(Path.Combine(execution.Root, "held"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            }
            cancellation.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => cancellation.Token.ThrowIfCancellationRequested());
            execution.Complete();
            execution.Dispose();
            Assert.IsNotNull(execution.CleanupWarning);
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
            if (failure == "marker") StringAssert.Contains(execution.CleanupWarning, "cancellation marker");
            else
            {
                StringAssert.Contains(execution.CleanupWarning, "could not be removed");
                Assert.IsTrue(Directory.Exists(execution.Root));
            }
        }
        finally { held?.Dispose(); }
    }

    [TestMethod]
    public async Task NativeBuildHost_CallerCancellationDuringActualPreprocessingStage_StopsOwnedHostBeforeFixtureCleanup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var fixture = new NativeFixture(false);
        var package = Path.Combine(fixture.Root, "package");
        var content = Path.Combine(package, "contentFiles/any/net10.0");
        var feed = Path.Combine(fixture.Root, "feed");
        Directory.CreateDirectory(content);
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(package, "Cancellation.Content.nuspec"), "<package><metadata><id>Cancellation.Content</id><version>1.0.0</version><authors>Test</authors><description>Actual staged cancellation</description><contentFiles><files include=\"any/net10.0/*.cs.pp\" buildAction=\"None\" copyToOutput=\"false\" /></contentFiles></metadata></package>");
        var text = "// " + new string('x', 4 * 1024 * 1024) + " $rootnamespace$";
        for (var index = 0; index < 32; index++) File.WriteAllText(Path.Combine(content, $"probe{index:D2}.cs.pp"), text);
        System.IO.Compression.ZipFile.CreateFromDirectory(package, Path.Combine(feed, "Cancellation.Content.1.0.0.nupkg"));
        var project = fixture.PathInFork("Cancellation.csproj");
        var protectedDirectory = Path.Combine(fixture.Source, "protected");
        fixture.WriteProject(project, new XElement("PropertyGroup", new XElement("RestoreSources", feed),
                new XElement("RestorePackagesPath", Path.Combine(fixture.Root, "packages")), new XElement("ContentPreprocessorOutputDirectory", protectedDirectory)) +
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Cancellation.Content"), new XAttribute("Version", "1.0.0"))).ToString());
        var pidPath = Path.Combine(fixture.Evidence, "buildhost.pid");
        // Exec starts its ordinary command shell under the actual BuildHost. Observe
        // that shell's parent; no property-function expansion or product PID hook.
        var observeCommand = "printf '%s' \"$PPID\" > '" + pidPath.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        if (OperatingSystem.IsWindows())
        {
            var observer = Path.Combine(fixture.Evidence, "observe-buildhost.ps1");
            File.WriteAllText(observer, "param([string]$OutputPath)\n" +
                "$shell = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID)).ParentProcessId\n" +
                "$hostId = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $shell)).ParentProcessId\n" +
                "[IO.File]::WriteAllText($OutputPath, [string]$hostId)\n");
            observeCommand = "pwsh -NoProfile -File \"" + observer + "\" -OutputPath \"" + pidPath + "\"";
        }
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "ObserveOwnedNativeBuildHost"), new XAttribute("BeforeTargets", "RunProduceContentAssets"),
                new XElement("Exec", new XAttribute("Command", observeCommand)))).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var diagnostics = new WorkspaceDiagnosticsSink(20);
        var loading = new WorkspaceSessionLoader().CreateAndOpenAsync(Guid.NewGuid().ToString("N"), project, null,
            diagnostics, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, cancellation.Token);
        System.Diagnostics.Process? host = null;
        try
        {
            var executions = Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions");
            var observedStage = false;
            while (!loading.IsCompleted)
            {
                if (Directory.Exists(executions) && Directory.GetFiles(executions, "*.cs", SearchOption.AllDirectories).Any(path => new FileInfo(path).Length > 0))
                {
                    observedStage = true;
                    break;
                }
                await Task.Delay(5, timeout.Token);
            }
            Assert.IsTrue(observedStage, "Cancellation must be triggered during an actual native preprocessing stage. " + string.Join(" | ", diagnostics.Queue.Select(item => item.Message)));
            host = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(pidPath).Trim(), System.Globalization.CultureInfo.InvariantCulture));
            Assert.IsFalse(host.HasExited);
            Assert.IsFalse(Directory.Exists(protectedDirectory));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => loading);
            await host.WaitForExitAsync(timeout.Token);
            Assert.IsFalse(Directory.Exists(protectedDirectory));
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                var result = await loading;
                result.Workspace.Dispose();
                result.Lease.Dispose();
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException ex) when (ex is IPublicMessageException) { }
            if (host is { HasExited: false })
            {
                // Failure-only cleanup of the exact host reported by this owned fixture.
                host.Kill(entireProcessTree: true);
                await host.WaitForExitAsync(CancellationToken.None);
            }
            host?.Dispose();
        }
    }

    [TestMethod]
    public async Task NativeLoader_SetupFailure_ReleasesUnstartedExecutionAndOwnership()
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        var expected = new InvalidOperationException("Caller dictionary cannot be read.");
        var properties = new UnreadableProperties(expected);
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new WorkspaceSessionLoader().CreateAndOpenAsync(
            Guid.NewGuid().ToString("N"), project, properties, new WorkspaceDiagnosticsSink(0),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None));
        Assert.AreSame(expected, failure);
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
        Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions")).Length);
    }

    private sealed class UnreadableProperties(Exception failure) : IDictionary<string, string>
    {
        public string this[string key] { get => throw failure; set => throw failure; }
        public ICollection<string> Keys => throw failure;
        public ICollection<string> Values => throw failure;
        public int Count => 1;
        public bool IsReadOnly => true;
        public void Add(string key, string value) => throw failure;
        public void Add(KeyValuePair<string, string> item) => throw failure;
        public void Clear() => throw failure;
        public bool Contains(KeyValuePair<string, string> item) => throw failure;
        public bool ContainsKey(string key) => throw failure;
        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) => throw failure;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => throw failure;
        public bool Remove(string key) => throw failure;
        public bool Remove(KeyValuePair<string, string> item) => throw failure;
        public bool TryGetValue(string key, out string value) => throw failure;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw failure;
    }

    [TestMethod]
    public async Task ForkOwnership_PhysicalAliasSharesCriticalSectionForSweepAndCleanup()
    {
        using var fixture = new NativeFixture(false);
        var aliases = Path.Combine(fixture.Root, "aliases");
        Directory.CreateDirectory(aliases);
        var alias = Path.Combine(aliases, "20000101T000000000Z-alias");
        WorkspaceForkGraphReplayTests.CreateLink(alias, fixture.Fork);
        using var held = await WorkspaceForkApplyService.AcquireForkApplyLockAsync(fixture.Fork, CancellationToken.None);
        using var aliasOwnership = WorkspaceForkApplyService.TryAcquireForkApplyLock(alias);
        Assert.IsNull(aliasOwnership, "Physical aliases must use the same existing fork ownership key.");
        WorkspaceForkApplyService.SweepExpiredForks(aliases, 1, DateTimeOffset.UtcNow);
        Assert.IsTrue(Directory.Exists(alias), "Expiry must not delete an alias while its physical fork is owned.");
        var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, new PreviewStore(), null!, null!, DotnetCommandRunner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance);
        var cleanup = typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var warnings = new List<string>();
        cleanup.Invoke(service, [null, alias, warnings, CancellationToken.None]);
        Assert.AreEqual(1, warnings.Count);
        Assert.IsTrue(Directory.Exists(alias));
        held.Dispose();
        WorkspaceForkApplyService.SweepExpiredForks(aliases, 1, DateTimeOffset.UtcNow);
        Assert.IsFalse(Directory.Exists(alias), "An unowned expired alias must remain eligible for ordinary cleanup.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedFork_MissingConfiguration_RefusesPublicReopen(bool owned)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        if (owned)
        {
            ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
            File.Delete(fixture.PathInFork(ForkAdmissionMaterial.DirectoryName + "/config.json"));
        }
        else
        {
            var inert = fixture.PathInFork(ForkAdmissionMaterial.DirectoryName);
            Directory.CreateDirectory(inert);
            File.WriteAllText(Path.Combine(inert, "guard.targets"), "<Project><!-- Ordinary user data, not owned admission material. --></Project>");
        }
        var before = File.ReadAllBytes(project);
        if (owned)
        {
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => WorkspaceManager.LoadAsync(project, timeout.Token));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(project));
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
        }
        else
        {
            var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("Content", "Always", "source", false)]
    [DataRow("Content", "Always", "source", true)]
    [DataRow("None", "Always", "source", false)]
    [DataRow("None", "Always", "source", true)]
    [DataRow("Compile", "Always", "source", false)]
    [DataRow("Compile", "Always", "source", true)]
    [DataRow("EmbeddedResource", "Always", "source", false)]
    [DataRow("EmbeddedResource", "Always", "source", true)]
    [DataRow("ReferenceCopyLocalPaths", "Always", "source", false)]
    [DataRow("ReferenceCopyLocalPaths", "Always", "source", true)]
    [DataRow("ContentLink", "Always", "source", false)]
    [DataRow("ContentLink", "Always", "source", true)]
    [DataRow("content", "Always", "source", false)]
    [DataRow("content", "Always", "source", true)]
    [DataRow("Content", "PreserveNewest", "source", false)]
    [DataRow("Content", "PreserveNewest", "source", true)]
    [DataRow("Content", "IfDifferent", "source", false)]
    [DataRow("Content", "IfDifferent", "source", true)]
    [DataRow("Content", "always", "source", false)]
    [DataRow("Content", "always", "source", true)]
    [DataRow("Content", "preservenewest", "source", false)]
    [DataRow("Content", "preservenewest", "source", true)]
    [DataRow("Content", "ifdifferent", "source", false)]
    [DataRow("Content", "ifdifferent", "source", true)]
    [DataRow("Content", "Always", "target-wins", false)]
    [DataRow("Content", "Always", "target-wins", true)]
    [DataRow("Content", "Always", "inactive", false)]
    [DataRow("Content", "Always", "inactive", true)]
    public async Task NativeCopyMetadata_RealSdkMembershipAndDestination_PreserveRolesAndControls(string role, string mode, string shape, bool guarded)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        File.WriteAllText(fixture.PathInFork("input.txt"), "native metadata bytes");
        var item = new XElement(role == "ContentLink" ? "Content" : role, new XAttribute("Include", "input.txt"),
            new XAttribute("CopyToOutputDirectory", shape == "inactive" ? "Never" : mode));
        if (role == "ReferenceCopyLocalPaths")
            item.Add(new XAttribute("DestinationSubDirectory", "../../"));
        else if (role == "ContentLink" || shape == "inactive")
            item.Add(new XAttribute("Link", "../../protected.txt"));
        else
        {
            item.Add(new XAttribute("TargetPath", shape == "target-wins" ? "safe.txt" : "../../protected.txt"));
            if (shape == "target-wins") item.Add(new XAttribute("Link", "../../protected.txt"));
        }
        fixture.WriteProject(project, new XElement("ItemGroup", item).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        var config = guarded ? ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token) : null;
        using var execution = config is null ? null : await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddSeconds(45), timeout.Token);
        var consumer = role == "ReferenceCopyLocalPaths" ? "_CopyFilesMarkedCopyLocal"
            : mode.Equals("PreserveNewest", StringComparison.OrdinalIgnoreCase) ? "_CopyOutOfDateSourceItemsToOutputDirectory"
            : mode.Equals("IfDifferent", StringComparison.OrdinalIgnoreCase) ? "_CopyDifferingSourceItemsToOutputDirectory"
            : "_CopyOutOfDateSourceItemsToOutputDirectoryAlways";
        string[] args = ["msbuild", project, "-nologo", "-nr:false", "-t:PrepareForBuild;GetCopyToOutputDirectoryItems;" + consumer];
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project, execution?.Arguments(args) ?? args, timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        var output = shape == "target-wins" ? fixture.PathInFork("bin/safe.txt")
            : role == "ReferenceCopyLocalPaths" ? Path.Combine(fixture.Source, "input.txt") : fixture.ProtectedOutput;
        if (shape == "inactive")
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            Assert.IsFalse(File.Exists(output));
        }
        else if (guarded && shape == "source")
        {
            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(File.Exists(output), "Admission must precede the unchanged real SDK Copy.");
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
            Assert.AreEqual("native metadata bytes", File.ReadAllText(output), "The actual SDK owns item membership and TargetPath/Link precedence.");
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ProjectImportInstallation_CancellationPreservesOriginalAndRemovesTemporaryFile(int boundary)
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.custombuild");
        fixture.WriteProject(project, "<!-- Preserve original bytes -->");
        var guard = fixture.PathInFork("guard.targets");
        File.WriteAllText(guard, "<Project />");
        var before = File.ReadAllBytes(project);
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        Assert.ThrowsExactly<OperationCanceledException>(() => ForkProjectImports.Install(project, fixture.Fork, guard, () =>
        {
            if (++checks == boundary) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }));
        Assert.AreEqual(boundary, checks);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(project));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Fork, "*.tmp").Length);
        Assert.IsFalse(File.Exists(fixture.ProtectedOutput));
    }

    [TestMethod]
    public async Task RetainedFork_ActualNativeCommandCancellation_StopsObservedChildAndReleasesOwnership()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId);
        var before = File.ReadAllBytes(project);
        var observer = Path.Combine(fixture.Evidence, "native-child.ps1");
        var observed = Path.Combine(fixture.Evidence, "child.pid");
        File.WriteAllText(observer, "param([string]$OutputPath)\n[IO.File]::WriteAllText($OutputPath, [string]$PID)\nStart-Sleep -Seconds 30\n");
        var command = "pwsh -NoProfile -File \"" + observer + "\" -OutputPath \"" + observed + "\"";
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "CancellationProbe"),
                new XElement("Exec", new XAttribute("Command", command)))).ToString());
        var running = GatedCommandExecutor.ExecuteAsync(loaded.WorkspaceId, project,
            ["msbuild", project, "-t:CancellationProbe", "-nr:false", "-nologo"], TimeSpan.FromSeconds(45), cancellation.Token);
        System.Diagnostics.Process? child = null;
        try
        {
            while (!File.Exists(observed) && !running.IsCompleted) await Task.Delay(10, timeout.Token);
            Assert.IsTrue(File.Exists(observed), "The native command must actually reach its owned child before cancellation.");
            child = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(observed), System.Globalization.CultureInfo.InvariantCulture));
            Assert.IsFalse(child.HasExited);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => running);
            await child.WaitForExitAsync(timeout.Token);
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(project));
            Assert.IsFalse(File.Exists(fixture.ProtectedOutput));
        }
        finally
        {
            cancellation.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
            child?.Dispose();
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    private sealed class NativeFixture : IDisposable
    {
        internal string Root { get; }
        internal string Source => Path.Combine(Root, "source");
        internal string Fork => Path.Combine(Source, "fork");
        internal string Evidence => Path.Combine(Root, "evidence");
        internal string ProtectedOutput => Path.Combine(Source, "protected.txt");

        internal NativeFixture(bool quoted)
        {
            Root = Path.Combine(TestTempRoot.Current, "fork-native-" + (quoted ? "quoted'-" : "") + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Fork);
            Directory.CreateDirectory(Evidence);
        }

        internal string PathInFork(string name) => Path.Combine(Fork, name);

        internal void WriteProject(string path, string body)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems>
                    <OutDir>bin/</OutDir><NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                """ + body + "</Project>");
        }

        internal async Task RestoreAsync(string path, IDotnetCommandRunner runner, CancellationToken ct)
        {
            var restore = await runner.RunAsync(Fork, path, ["restore", path, "--nologo", "-nr:false"], ct);
            Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
        }

        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }
}
