using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkNativeAdmissionTests : ForkNativeAdmissionTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

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
        using var execution = await ForkAdmissionExecution.CreateAsync(configuration, DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System, timeout.Token);
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, root,
            execution.Arguments(["msbuild", root, "-t:Query", "-m:2", "-nr:false", "-nologo", "-p:ProofEvidence=" + fixture.Evidence]), timeout.Token);
        execution.Complete();
        Assert.IsTrue(result.Succeeded, result.Describe());
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
        using var execution = await ForkAdmissionExecution.CreateAsync(configuration, DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System, timeout.Token);
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project,
            execution.Arguments(["msbuild", project, "-t:GetTargetFrameworksWithPlatformFromInnerBuilds;DispatchToInnerBuilds",
                "-p:InnerTargets=InnerConsumer", "-p:ProofEvidence=" + fixture.Evidence, "-m:2", "-nr:false", "-nologo"]), timeout.Token);
        execution.Complete();
        Assert.IsTrue(result.Succeeded, result.Describe());
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
        using var execution = configuration is null ? null : await ForkAdmissionExecution.CreateAsync(configuration, DateTimeOffset.UtcNow.AddMinutes(2), TimeProvider.System, timeout.Token);
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
            Assert.IsFalse(result.Succeeded, result.Describe());
            Assert.IsFalse(File.Exists(protectedOutput));
            Assert.ThrowsExactly<PublicInvalidOperationException>(execution!.ThrowIfRefused);
        }
        else
        {
            Assert.IsTrue(result.Succeeded, result.Describe());
            if (sourceOutput) Assert.AreEqual("child", File.ReadAllText(protectedOutput));
            else Assert.AreEqual("child", File.ReadAllText(Path.Combine(childDirectory, "bin", "child.txt")));
            var parentItems = File.ReadAllLines(Path.Combine(fixture.Evidence, "full-Parent.txt"));
            Assert.AreEqual(mode == "default" ? 4 : 2, parentItems.Length, "Full membership must remain the SDK's transitive/private selection.");
            Assert.AreEqual(1, File.ReadAllLines(Path.Combine(fixture.Evidence, "this-Child.txt")).Length);
        }
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
}
