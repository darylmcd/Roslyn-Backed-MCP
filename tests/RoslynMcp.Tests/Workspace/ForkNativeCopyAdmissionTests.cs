using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkNativeCopyAdmissionTests : ForkNativeAdmissionTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

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
            DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System, timeout.Token);
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
            Assert.IsTrue(result.Succeeded, result.Describe());
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
        using var execution = config is null ? null : await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System, timeout.Token);
        // SDK 10 touches the CopyUpToDateMarker inside _CopyFilesMarkedCopyLocal; SDK 11 records the
        // copy there and touches the marker later, in CopyFilesToOutputDirectory. Request both targets
        // so the real native consumer runs on either SDK (SkipCopyBuildProduct: nothing is compiled).
        string[] arguments = marker
            ? ["msbuild", project, "-nologo", "-nr:false", "-p:SkipCopyBuildProduct=true", "-t:PrepareForBuild;" + target + ";CopyFilesToOutputDirectory"]
            : ["msbuild", project, "-nologo", "-nr:false", "-t:PrepareForBuild;" + target];
        var result = await DotnetCommandRunner.RunAsync(fixture.Fork, project, execution?.Arguments(arguments) ?? arguments, timeout.Token);
        execution?.Complete();
        TestContext.WriteLine(result.StdOut + result.StdErr);
        if (consumer.EndsWith("-disabled", StringComparison.Ordinal))
        {
            Assert.IsTrue(result.Succeeded, result.Describe());
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
            Assert.IsTrue(result.Succeeded, result.Describe());
            Assert.IsTrue(File.Exists(output), "The real native consumer must independently produce its output.");
            if (!marker) Assert.AreEqual("native late consumer bytes", File.ReadAllText(output));
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
        using var execution = config is null ? null : await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddSeconds(45), TimeProvider.System, timeout.Token);
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
            Assert.IsTrue(result.Succeeded, result.Describe());
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
            Assert.IsTrue(result.Succeeded, result.Describe());
            Assert.AreEqual("native metadata bytes", File.ReadAllText(output), "The actual SDK owns item membership and TargetPath/Link precedence.");
        }
    }
}
