using System.Xml.Linq;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerInteropProducerTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    public async Task CompilerReferenceInputs_ObserveGeneratedInteropMetadataAndExecutionOverride(bool executionOverride, bool directOverride, bool mutateAfterTask)
        => await ExerciseAsync(executionOverride, directOverride, mutateAfterTask, normalizeOwnedLoad: true);

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    public async Task SelectiveTaskChannel_ObservesActualInputsAndOptions(bool executionOverride, bool directOverride, bool mutateAfterTask)
        => await ExerciseAsync(executionOverride, directOverride, mutateAfterTask, normalizeOwnedLoad: false);

    private static async Task ExerciseAsync(bool executionOverride, bool directOverride, bool mutateAfterTask, bool normalizeOwnedLoad)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerInteropProducerTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            foreach (var name in new[] { "App", "Library", "Contract" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")));
                if (name != "Contract") project.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                    new XAttribute("Include", name == "App" ? "../Library/Library.csproj" : "../Contract/Contract.csproj"))));
                if (name == "App")
                {
                    if (normalizeOwnedLoad && !executionOverride && !mutateAfterTask)
                    {
                        project.Element("PropertyGroup")?.Add(new XElement("CompilerResponseFile", "authority.rsp"));
                        await File.WriteAllTextAsync(Path.Combine(directory, "authority.rsp"), "/define:ISSUED_RESPONSE\n");
                    }
                    project.Add(new XElement("ItemDefinitionGroup", new XElement("ProjectReference", new XElement("EmbedInteropTypes", "true"))));
                    if (executionOverride)
                        project.Add(new XElement("Target", new XAttribute("Name", "OverrideGeneratedInterop"),
                            new XAttribute("AfterTargets", "IncludeTransitiveProjectReferences"),
                            new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Update", "@(ProjectReference)"),
                                new XAttribute("Condition", "'%(ProjectReference.Filename)' == '" + (directOverride ? "Library" : "Contract") + "'"),
                                new XElement("EmbedInteropTypes", "false")))));
                    // These are the actual items consumed by the SDK's Csc References parameter.
                    project.Add(new XElement("Target", new XAttribute("Name", "RecordCompilerReferenceInputs"),
                        new XAttribute("BeforeTargets", "CoreCompile"), new XElement("WriteLinesToFile",
                            new XAttribute("File", "$(MSBuildProjectDirectory)/compiler-reference-inputs.txt"),
                            new XAttribute("Lines", "@(ReferencePathWithRefAssemblies->'%(Filename)|%(EmbedInteropTypes)')"),
                            new XAttribute("Overwrite", "true"))));
                    if (mutateAfterTask)
                        project.Add(new XElement("Target", new XAttribute("Name", "ChangeStateAfterCompilerTask"),
                            new XAttribute("AfterTargets", "CoreCompile"),
                            new XElement("ItemGroup", new XElement("ReferencePathWithRefAssemblies",
                                new XAttribute("Update", "@(ReferencePathWithRefAssemblies)"),
                                new XElement("EmbedInteropTypes", "false"))),
                            new XElement("WriteLinesToFile", new XAttribute("File", "$(MSBuildProjectDirectory)/post-task-state.txt"),
                                new XAttribute("Lines", "@(ReferencePathWithRefAssemblies->'%(Filename)|%(EmbedInteropTypes)')"),
                                new XAttribute("Overwrite", "true"))));
                }
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"), project.ToString());
                await File.WriteAllTextAsync(Path.Combine(directory, "Entry.cs"), name == "App" ? "public class AppEntry { }" : $$"""
                    using System.Runtime.InteropServices;
                    [assembly: Guid("{{(name == "Library" ? "527E4788-D94D-40CC-88CA-CA4F740E32A1" : "A87E82ED-2093-44D2-89C9-3DB4DFE9DA59")}}")]
                    [assembly: ImportedFromTypeLib("{{name}}")]
                    [ComImport, Guid("5783904E-959A-4098-A539-765A8F1D3645"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                    public interface I{{name}} { void Invoke(); }
                    """);
            }
            var app = Path.Combine(root, "App", "App.csproj");
            var loadProperties = OwnedNativeLoadContext.Properties(app, new Dictionary<string, string>());
            var runner = new DotnetCommandRunner();
            var restore = await runner.RunAsync(root, app, MsBuildCommandProperties.Append(["restore", app, "--nologo"], loadProperties), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
            var build = await runner.RunAsync(root, app, MsBuildCommandProperties.Append(["build", app, "--nologo", "--no-restore"], loadProperties), CancellationToken.None);
            Assert.IsTrue(build.Succeeded, build.StdErr + build.StdOut);
            var items = await File.ReadAllLinesAsync(Path.Combine(root, "App", "compiler-reference-inputs.txt"));
            var contract = items.Single(item => item.StartsWith("Contract|", StringComparison.Ordinal));
            Assert.AreEqual("Contract|" + (executionOverride && !directOverride ? "false" : "true"), contract);
            var library = items.Single(item => item.StartsWith("Library|", StringComparison.Ordinal));
            Assert.AreEqual("Library|" + (executionOverride && directOverride ? "false" : "true"), library);
            if (!normalizeOwnedLoad)
            {
                RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
                var native = MSBuildWorkspace.Create(loadProperties);
                Exception? nativePrimary = null;
                try
                {
                    var original = await native.OpenProjectAsync(app);
                    var parsing = OwnedNativeCompilerInputContext.Create(original);
                    var toolsVersion = OwnedNativeCompilerInputContext.CaptureToolsVersion(app, native.Properties);
                    var observed = await NativeCompilerTaskInputs.ReadAsync(original, native.Properties, "net10.0", toolsVersion, ["Compile", "CoreCompile"], parsing, CancellationToken.None);
                    foreach (var target in new[] { "Library", "Contract" })
                    {
                        var input = observed.References.Single(reference => Path.GetFileNameWithoutExtension(reference.Path) == target);
                        Assert.AreEqual(!(executionOverride && target == (directOverride ? "Library" : "Contract")), input.EmbedInteropTypes);
                    }
                    Assert.IsTrue(observed.Arguments.Length > 0);
                }
                catch (Exception failure) { nativePrimary = failure; throw; }
                finally { OwnedResourceCleanup.Dispose("Selective compiler workspace cleanup failed.", nativePrimary, native); }
                return;
            }
            var policy = new OwnedTestWorkspacePathPolicy();
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("compiler-producer", app, OwnedNativeLoadContext.Properties(app, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? loadedPrimary = null;
            try
            {
                var normalized = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("compiler-producer", loaded.Context,
                    policy, CancellationToken.None);
                var repeated = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("compiler-producer", loaded.Context,
                    policy, CancellationToken.None);
                Assert.AreSame(normalized.Solution, repeated.Solution);
                var owner = normalized.Solution.Projects.Single(project => project.FilePath == app);
                foreach (var reference in owner.ProjectReferences)
                {
                    var target = normalized.Solution.GetProject(reference.ProjectId)
                        ?? throw new AssertFailedException("A normalized compiler edge has no target.");
                    var targetName = Path.GetFileNameWithoutExtension(target.FilePath);
                    Assert.AreEqual(!(executionOverride && targetName == (directOverride ? "Library" : "Contract")), reference.EmbedInteropTypes);
                }
                Assert.HasCount(2, owner.ProjectReferences.ToArray());
                Assert.AreSame(loaded.Context, NativeCompilerContextCertificate.Require(normalized.Solution));
                Task Authorize(IReadOnlyList<string> paths, CancellationToken token) => policy.ValidateAsync("compiler-producer", paths, token);
                async Task<byte[]?> Read(string path, CancellationToken token)
                {
                    await Authorize([path], token);
                    return await FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, token);
                }
                var authority = await RestoredCompilerInputAuthority.CaptureReferenceAuthorityAsync(normalized.Solution, [owner.Id],
                    Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), Authorize, CancellationToken.None);
                authority.AssertCurrent(owner);
                if (!executionOverride && !mutateAfterTask)
                {
                    // A semantically harmless response edit is still a new physical
                    // epoch; repeated issuance cannot silently replace its receipt.
                    await File.AppendAllTextAsync(Path.Combine(root, "App", "authority.rsp"), "# new compiler input epoch\n");
                    await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(() =>
                        EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("compiler-producer", loaded.Context, policy, CancellationToken.None));
                    var reloaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("compiler-reload", app, OwnedNativeLoadContext.Properties(app, new Dictionary<string, string>()), policy,
                        new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                    Exception? reloadPrimary = null;
                    try
                    {
                        var fresh = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("compiler-reload", reloaded.Context, policy, CancellationToken.None);
                        Assert.AreNotSame(loaded.Context, reloaded.Context);
                        Assert.AreSame(reloaded.Context, NativeCompilerContextCertificate.Require(fresh.Solution));
                        var freshRepeat = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("compiler-reload", reloaded.Context, policy, CancellationToken.None);
                        Assert.AreSame(fresh.Solution, freshRepeat.Solution);
                    }
                    catch (Exception failure) { reloadPrimary = failure; throw; }
                    finally { OwnedResourceCleanup.Dispose("Repeated compiler issuance fixture cleanup failed.", reloadPrimary, reloaded.Workspace, reloaded.Lease); }
                }
                if (mutateAfterTask)
                {
                    var post = await File.ReadAllLinesAsync(Path.Combine(root, "App", "post-task-state.txt"));
                    CollectionAssert.Contains(post, "Contract|false");
                    CollectionAssert.Contains(post, "Library|false");
                    Assert.IsTrue(owner.ProjectReferences.All(reference => reference.EmbedInteropTypes),
                        "Post-target item mutation cannot replace the actual trusted Csc task-input receipt.");
                }
            }
            catch (Exception failure) { loadedPrimary = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Compiler producer load cleanup failed.", loadedPrimary, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler producer probe cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
