using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed partial class ForkInvocationManagerAuthorityTests
{
    [TestMethod]
    public async Task InitialForkLoad_RejectsSupplementalReceiptFromAnotherOriginalSession()
    {
        var first = await NuGetInstallationFixture.CreateAsync("Owned.First", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/First.cs"] = "class First { }"u8.ToArray() });
        Exception? resourceFailure1 = null;
        try
        {
            var second = await NuGetInstallationFixture.CreateAsync("Owned.Second", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Second.cs"] = "class Second { }"u8.ToArray() });
            Exception? resourceFailure2 = null;
            try
            {
                var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
                var manager = CreateManager(policy, new FileWatcherService(NullLogger<FileWatcherService>.Instance), 4, new PreviewStore());
                Exception? resourceFailure3 = null;
                try
                {
                    var a = await manager.LoadAsync(Path.Combine(first.Root, "Owner.csproj"), first.LoadProperties, CancellationToken.None);
                    var b = await manager.LoadAsync(Path.Combine(second.Root, "Owner.csproj"), second.LoadProperties, CancellationToken.None);
                    var loader = (IWorkspaceEvaluationLoader)manager;
                    var source = manager.GetCurrentSolution(a.WorkspaceId);
                    var sourceLoadedPath = manager.GetStatus(a.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                    var layout = await PhysicalGraphLayout.CreateAsync(a.WorkspaceId, sourceLoadedPath, source, policy, CancellationToken.None);
                    var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(second.Root, "private"));
                    Exception? privateFailure = null;
                    try
                    {
                        var destination = NativePackageForkCopy.Copy(second.Root, destinationOwner, layout, sourceLoadedPath);
                        var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
                        var unrelatedReceipt = loader.CapturePackageReadAuthority(b.WorkspaceId, manager.GetCurrentSolution(b.WorkspaceId));
                        Assert.IsTrue(unrelatedReceipt.Files.Any(path => path.EndsWith("Second.cs", StringComparison.Ordinal)));
                        var capability = loader.CaptureForkInvocationAuthority(a.WorkspaceId, source, destination, properties, destinationOwner, layout);
                        using var scope = policy.Enter(capability);
                        using var ownedCopy = capability.EnterOwnedCopy(destinationOwner, layout);
                        await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => loader.LoadWithPropertiesAsync(destination, properties, unrelatedReceipt, capability, CancellationToken.None));
                        Assert.HasCount(2, manager.ListWorkspaces());
                        Assert.AreSame(source, manager.GetCurrentSolution(a.WorkspaceId));
                    }
                    catch (Exception failure)
                    {
                        privateFailure = failure;
                        throw;
                    }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure, destinationOwner);
                    }
                }
                catch (Exception cleanupFailure)
                {
                    resourceFailure3 = cleanupFailure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure3, manager);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure2 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure2, second);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure1 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure1, first);
        }
    }

    [TestMethod]
    public async Task NestedForkApply_RefusesBeforePreviewRetrievalOrDirectoryEffects()
    {
        const string key = "ROSLYNMCP_NESTED_REFUSAL_TEMP";
        var inherited = Environment.GetEnvironmentVariable(key);
        var isolated = inherited ?? Path.Combine(TestTempRoot.Current, "NestedForkRefusal", Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            if (inherited is null)
                Directory.CreateDirectory(isolated);
            await NativeConditionProcessTest.RunAsync(typeof(ForkInvocationManagerAuthorityTests), nameof(NestedForkApply_RefusesBeforePreviewRetrievalOrDirectoryEffects), new Dictionary<string, string?> { [key] = isolated, ["TEMP"] = isolated, ["TMP"] = isolated, ["TMPDIR"] = isolated }, () => AssertNestedForkRefusalAsync(isolated));
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            if (inherited is null)
                OwnedResourceCleanup.DisposePreservingFailure("Isolated nested fork effects cleanup failed.", primary, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(isolated)));
        }
    }

    private static async Task AssertNestedForkRefusalAsync(string isolated)
    {
        Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(isolated), Path.TrimEndingDirectorySeparator(Path.GetTempPath())), "The child must own its complete service namespace parent.");
        string[] NamespaceEffects() => new[]
        {
            PhysicalGraphNamespace.StagingParent,
            PhysicalGraphNamespace.ForkParent
        }.SelectMany(parent => Directory.Exists(parent) ? new[] { parent }.Concat(Directory.EnumerateFileSystemEntries(parent, "*", SearchOption.AllDirectories)) : []).Order(StringComparer.Ordinal).ToArray();
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
        Exception? resourceFailure4 = null;
        try
        {
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            var provider = CreateProvider(policy, new DotnetCommandRunner());
            Exception? resourceFailure5 = null;
            try
            {
                var manager = (WorkspaceManager)provider.GetRequiredService<IWorkspaceManager>();
                var original = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var source = manager.GetCurrentSolution(original.WorkspaceId);
                var loader = (IWorkspaceEvaluationLoader)manager;
                var sourceLoadedPath = manager.GetStatus(original.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, sourceLoadedPath, source, policy, CancellationToken.None);
                var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "uncreated"));
                Exception? privateFailure = null;
                try
                {
                    var destination = layout.MapTo(destinationOwner.Path, sourceLoadedPath);
                    var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, source, destination, OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>()), destinationOwner, layout);
                    var before = Directory.EnumerateFileSystemEntries(fixture.Root).Order(StringComparer.Ordinal).ToArray();
                    var namespaceBefore = NamespaceEffects();
                    using var scope = policy.Enter(capability);
                    using var ownedCopy = capability.EnterOwnedCopy(destinationOwner, layout);
                    await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => provider.GetRequiredService<IWorkspaceForkApplyService>().ApplyAsync(original.WorkspaceId, "unretrievable-preview", "keep", false, null, "nested-operation", CancellationToken.None));
                    CollectionAssert.AreEqual(before, Directory.EnumerateFileSystemEntries(fixture.Root).Order(StringComparer.Ordinal).ToArray());
                    CollectionAssert.AreEqual(namespaceBefore, NamespaceEffects(), "Nested refusal must precede isolated service namespace effects.");
                    Assert.HasCount(1, manager.ListWorkspaces());
                }
                catch (Exception failure)
                {
                    privateFailure = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure, destinationOwner);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure5 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure5, provider);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure4 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure4, fixture);
        }
    }

    [TestMethod]
    public Task ActualForkApply_UsesOriginalRootAndCacheGrantsThroughValidationOnly()
        => ObserveScopedForkAsync(actualTestProcess: false);

    // Exercise the real process and authority checks within the production budget,
    // independently of unrelated native-evaluation classes sharing this test host.
    [TestMethod]
    [DoNotParallelize]
    public Task ActualTestProcess_UsesOriginalRootAndCacheGrantsThroughValidation()
        => ObserveScopedForkAsync(actualTestProcess: true);

    private static async Task ObserveScopedForkAsync(bool actualTestProcess)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "// Both package and authored glob occurrences must remain valid compiler inputs."u8.ToArray() });
        Exception? resourceFailure6 = null;
        try
        {
            if (actualTestProcess)
            {
                var feed = Path.Combine(fixture.Root, "feed");
                var versions = await ResolvedTestPackageFeed.PopulateAsync(fixture.Root, feed, CancellationToken.None);
                var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
                var definition = System.Xml.Linq.XDocument.Load(projectPath);
                definition.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("IsTestProject", "true"), new System.Xml.Linq.XElement("AssemblySearchPaths", "{HintPathFromItem};{CandidateAssemblyFiles};{TargetFrameworkDirectory};{RawFileName}"), new System.Xml.Linq.XElement("DesignTimeAssemblySearchPaths", "$(AssemblySearchPaths)"), new System.Xml.Linq.XElement("RestoreSources", feed)));
                definition.Root.Add(new System.Xml.Linq.XElement("ItemGroup", versions.Select(version => new System.Xml.Linq.XElement("PackageReference", new System.Xml.Linq.XAttribute("Include", version.Key), new System.Xml.Linq.XAttribute("Version", version.Value)))));
                await File.WriteAllTextAsync(projectPath, definition.ToString());
                await File.WriteAllTextAsync(Path.Combine(fixture.Root, "ScopedForkTests.cs"), """
                    using Microsoft.VisualStudio.TestTools.UnitTesting;
                    [TestClass] public class ScopedForkTests
                    {
                        [TestMethod] public void ReviewedValueIsExecuted() => Assert.AreEqual(2, new Entry().Value, "reviewed-fork-value-required");
                    }
                    """);
                var restore = await new DotnetCommandRunner().RunAsync(fixture.Root, projectPath, MsBuildCommandProperties.Append(["restore", projectPath, "--nologo", "--source", feed, "--packages", fixture.Cache], fixture.LoadProperties), CancellationToken.None);
                Assert.IsTrue(restore.Succeeded, "The real test SDK/adapter/framework must restore from the verified local feed.");
            }

            var code = Path.Combine(fixture.Root, "Entry.cs");
            await File.WriteAllTextAsync(code, "public class Entry { public int Value => 1; }");
            if (actualTestProcess)
                await AssertOriginalTestBodyFailsAsync(fixture);
            var raw = new OriginalOnlyPolicy(fixture.Root);
            var policy = new ForkInvocationPathPolicy(raw);
            var runner = new CountingCommandRunner();
            WorkspaceForkApplyResultDto? retainedFork = null;
            var provider = CreateProvider(policy, runner);
            Exception? resourceFailure7 = null;
            try
            {
                var manager = provider.GetRequiredService<IWorkspaceManager>();
                var source = await ((WorkspaceManager)manager).LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var original = manager.GetCurrentSolution(source.WorkspaceId);
                Assert.AreEqual(2, original.Projects.Single().Documents.Count(document => document.Name == "Input.cs"), "The native package item and authored glob must both remain in this authority regression.");
                if (actualTestProcess)
                {
                    var native = EvaluatedBuildGraph.Resolve(original.Projects.Single(), CancellationToken.None);
                    var adapterTarget = native.BuildInputs.Definitions.Single(path => path.EndsWith("MSTest.TestAdapter.targets", StringComparison.OrdinalIgnoreCase) && FileSystemPath.IsStrictDescendant(fixture.Cache, path));
                    var selectedDirectory = Path.GetDirectoryName(adapterTarget)!;
                    var selected = Path.Combine(selectedDirectory, "MSTestAdapter.PlatformServices.dll");
                    var overlapping = Path.Combine(Path.GetDirectoryName(selectedDirectory)!, "net462", "MSTestAdapter.PlatformServices.dll");
                    Assert.IsFalse(FileSystemPath.Comparer.Equals(selected, overlapping));
                    Assert.IsTrue(File.Exists(selected));
                    Assert.IsTrue(File.Exists(overlapping));
                    Assert.IsTrue(native.PathEvidence.InputFiles.Contains(selected, FileSystemPath.Comparer));
                    Assert.IsTrue(native.PathEvidence.InputFiles.Contains(overlapping, FileSystemPath.Comparer), "The overlapping cache candidate must remain part of native input authority.");
                }

                var originalCompilation = await original.Projects.Single().GetCompilationAsync();
                Assert.IsNotNull(originalCompilation);
                var originalErrors = originalCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
                Assert.IsEmpty(originalErrors, "The source fixture must compile before exercising a successful fork: " + string.Join(";", originalErrors.Select(error => error.Id + ":" + error.GetMessage())));
                var version = manager.GetCurrentVersion(source.WorkspaceId);
                var document = original.Projects.Single().Documents.Single(document => FileSystemPath.Comparer.Equals(document.FilePath, code));
                var modified = original.WithDocumentText(document.Id, SourceText.From("public class Entry { public int Value => 2; }"));
                var originalBytes = await File.ReadAllBytesAsync(code);
                var originalAssets = await File.ReadAllBytesAsync(fixture.Assets);
                raw.SourceId = source.WorkspaceId;
                var token = provider.GetRequiredService<IPreviewStore>().Store(source.WorkspaceId, original, modified, version, "scoped current grant fork", [], PreviewKind.Unspecified);
                retainedFork = await provider.GetRequiredService<IWorkspaceForkApplyService>().ApplyAsync(source.WorkspaceId, token, "keep", true, actualTestProcess ? "FullyQualifiedName~ScopedForkTests" : null, "current-grants", CancellationToken.None);
                var result = retainedFork;
                Assert.IsTrue(result.Success, "The actual native restore, reload and validation must share the live original grants. " + System.Text.Json.JsonSerializer.Serialize(new { result.Validation, result.ExplicitTestRun }));
                Assert.IsTrue(result.Retained);
                if (actualTestProcess)
                {
                    Assert.IsNotNull(result.ExplicitTestRun);
                    Assert.AreEqual(1, result.ExplicitTestRun.Total, "The isolated native test process must execute the selected real test.");
                    Assert.AreEqual(1, result.ExplicitTestRun.Passed);
                }

                Assert.IsNotNull(result.ForkWorkspaceId);
                Assert.IsTrue(raw.Scopes.Count > 0);
                Assert.IsTrue(raw.Scopes.All(id => id == source.WorkspaceId));
                CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(code));
                CollectionAssert.AreEqual(originalAssets, await File.ReadAllBytesAsync(fixture.Assets));
                Assert.AreSame(original, manager.GetCurrentSolution(source.WorkspaceId));
                Assert.AreEqual(version, manager.GetCurrentVersion(source.WorkspaceId));
                var fork = manager.GetCurrentSolution(result.ForkWorkspaceId);
                var forkCode = fork.Projects.Single().Documents.Single(document => document.Name == "Entry.cs");
                StringAssert.Contains((await forkCode.GetTextAsync()).ToString(), "Value => 2");
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => manager.ReloadAsync(result.ForkWorkspaceId, CancellationToken.None));
                Assert.AreSame(fork, manager.GetCurrentSolution(result.ForkWorkspaceId));
                if (actualTestProcess)
                {
                    var commandsBefore = runner.Count;
                    var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => provider.GetRequiredService<ITestRunnerService>().PrepareTestRunAsync(result.ForkWorkspaceId, null, "FullyQualifiedName~ScopedForkTests", CancellationToken.None));
                    Assert.AreEqual("The nearest global.json cannot be established within the currently authorized workspace. Place an explicit global.json inside that workspace or authorize the actual inherited configuration, then retry.", refusal.Message);
                    Assert.AreEqual(commandsBefore, runner.Count, "Detached configuration refusal must precede restore or test processes.");
                    Assert.AreSame(fork, manager.GetCurrentSolution(result.ForkWorkspaceId));
                    Assert.AreSame(original, manager.GetCurrentSolution(source.WorkspaceId));
                    Assert.AreEqual(version, manager.GetCurrentVersion(source.WorkspaceId));
                    CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(code));
                    CollectionAssert.AreEqual(originalAssets, await File.ReadAllBytesAsync(fixture.Assets));
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure7 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure7, provider, OwnedResourceCleanup.FromAction(() =>
                {
                    if (retainedFork?.ForkPath is { } path)
                        TestFixtureFileSystem.DeleteDirectoryIfExists(path);
                }));
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure6 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure6, fixture);
        }
    }

    private static async Task AssertOriginalTestBodyFailsAsync(NuGetInstallationFixture fixture)
    {
        var results = Path.Combine(TestTempRoot.Current, "original-fork-test-" + Guid.NewGuid().ToString("N"));
        var resultsOwner = PhysicalDirectoryScope.CreateOwnedRoot(results);
        Exception? resourceFailure8 = null;
        try
        {
            Exception? primary = null;
            try
            {
                var project = Path.Combine(fixture.Root, "Owner.csproj");
                var execution = await new DotnetCommandRunner().RunAsync(fixture.Root, project, MsBuildCommandProperties.Append(["test", project, "--no-restore", "--nologo", "--filter", "FullyQualifiedName~ScopedForkTests", "--logger", "trx", "--results-directory", results], fixture.LoadProperties), CancellationToken.None);
                var parsed = await TestRunOutputReader.ParseAsync(resultsOwner, execution, CancellationToken.None);
                Assert.AreEqual(1, execution.ExitCode);
                Assert.AreEqual(1, parsed.Total, "The original fixture must execute its test body, not fail during adapter initialization.");
                Assert.AreEqual(1, parsed.Failed);
                Assert.AreEqual(0, parsed.Passed);
                Assert.AreEqual(0, parsed.Skipped);
                Assert.HasCount(1, parsed.Failures);
                Assert.AreEqual("ScopedForkTests.ReviewedValueIsExecuted", parsed.Failures[0].FullyQualifiedName);
                StringAssert.Contains(parsed.Failures[0].Message, "Assert.AreEqual");
                StringAssert.Contains(parsed.Failures[0].Message, "reviewed-fork-value-required");
            }
            catch (Exception failure)
            {
                primary = failure;
                throw;
            }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Original test proof cleanup failed.", primary, CleanupFailureCollector.FromAction(() => resultsOwner.Delete(CancellationToken.None)));
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure8 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure8, resultsOwner,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(results)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExternalReload_InvalidatesExactSourceOrDestinationSnapshot(bool reloadDestination)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
        Exception? resourceFailure9 = null;
        try
        {
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            var manager = CreateManager(policy, new FileWatcherService(NullLogger<FileWatcherService>.Instance), 2, new PreviewStore());
            IDisposable? managerFallback = manager;
            Exception? resourceFailure10 = null;
            try
            {
                var original = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var source = manager.GetCurrentSolution(original.WorkspaceId);
                var loader = (IWorkspaceEvaluationLoader)manager;
                var sourceLoadedPath = manager.GetStatus(original.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, sourceLoadedPath, source, policy, CancellationToken.None);
                var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "private"));
                IDisposable? invocationScope = null;
                IDisposable? copyScope = null;
                Exception? privateFailure = null;
                try
                {
                    var destination = NativePackageForkCopy.Copy(fixture.Root, destinationOwner, layout, sourceLoadedPath);
                    var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
                    var restored = await new DotnetCommandRunner().RunAsync(Path.GetDirectoryName(destination)!, destination, MsBuildCommandProperties.Append(["restore", destination, "--nologo", "--source", Path.Combine(fixture.Root, "feed"), "--packages", fixture.Cache], properties), CancellationToken.None);
                    Assert.IsTrue(restored.Succeeded);
                    var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, source, destination, properties, destinationOwner, layout);
                    invocationScope = policy.Enter(capability);
                    copyScope = capability.EnterOwnedCopy(destinationOwner, layout);
                    var fork = await loader.LoadWithPropertiesAsync(destination, properties, SupplementalPackageReadAuthority.None, capability, CancellationToken.None);
                    await capability.AssertCurrentAsync(CancellationToken.None);
                    var forkBefore = manager.GetCurrentSolution(fork.WorkspaceId);
                    var reloadedId = reloadDestination ? fork.WorkspaceId : original.WorkspaceId;
                    Task external;
                    using (ExecutionContext.SuppressFlow())
                        external = Task.Run(() => manager.ReloadAsync(reloadedId, CancellationToken.None));
                    await external;
                    Assert.AreNotSame(reloadDestination ? forkBefore : source, manager.GetCurrentSolution(reloadedId), "The independent native reload must actually publish a new snapshot before authority is checked.");
                    await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => capability.AssertCurrentAsync(CancellationToken.None));
                    Assert.HasCount(2, manager.ListWorkspaces(), "Refusing stale authority must not close either independently current session.");
                }
                catch (Exception failure)
                {
                    privateFailure = failure;
                    throw;
                }
                finally
                {
                    var managerToDispose = managerFallback;
                    managerFallback = null;
                    OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure,
                        managerToDispose, destinationOwner, copyScope, invocationScope);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure10 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure10, managerFallback);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure9 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure9, fixture);
        }
    }

    [TestMethod]
    public async Task SourceClosedDuringAsynchronousApproval_RefusesBeforePrivateNativeLoad()
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
        Exception? resourceFailure11 = null;
        try
        {
            var raw = new CallbackPolicy();
            var policy = new ForkInvocationPathPolicy(raw);
            var manager = CreateManager(policy, new FileWatcherService(NullLogger<FileWatcherService>.Instance), 2, new PreviewStore());
            Exception? resourceFailure12 = null;
            try
            {
                var original = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var loader = (IWorkspaceEvaluationLoader)manager;
                var source = manager.GetCurrentSolution(original.WorkspaceId);
                var sourceLoadedPath = manager.GetStatus(original.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, sourceLoadedPath, source, policy, CancellationToken.None);
                var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "private"));
                Exception? privateFailure = null;
                try
                {
                    var destination = NativePackageForkCopy.Copy(fixture.Root, destinationOwner, layout, sourceLoadedPath);
                    var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
                    var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, source, destination, properties, destinationOwner, layout);
                    var calls = 0;
                    raw.OnValidate = async id =>
                    {
                        Assert.AreEqual(original.WorkspaceId, id);
                        calls++;
                        await Task.Yield();
                        Assert.IsTrue(manager.Close(original.WorkspaceId));
                    };
                    using var scope = policy.Enter(capability);
                    using var ownedCopy = capability.EnterOwnedCopy(destinationOwner, layout);
                    await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => loader.LoadWithPropertiesAsync(destination, properties, SupplementalPackageReadAuthority.None, capability, CancellationToken.None));
                    Assert.AreEqual(1, calls, "Source closure must be observed immediately after the actual asynchronous approval.");
                    Assert.HasCount(0, manager.ListWorkspaces());
                }
                catch (Exception failure)
                {
                    privateFailure = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure, destinationOwner);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure12 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure12, manager);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure11 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure11, fixture);
        }
    }

    [TestMethod]
    [DataRow("path")]
    [DataRow("globals")]
    public async Task AllocatedDestination_MustMatchIssuedPathAndGlobals(string mismatch)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
        Exception? resourceFailure13 = null;
        try
        {
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            var manager = CreateManager(policy, new FileWatcherService(NullLogger<FileWatcherService>.Instance), 2, new PreviewStore());
            Exception? resourceFailure14 = null;
            try
            {
                var original = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var loader = (IWorkspaceEvaluationLoader)manager;
                var sourceLoadedPath = manager.GetStatus(original.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, sourceLoadedPath, manager.GetCurrentSolution(original.WorkspaceId), policy, CancellationToken.None);
                var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "private"));
                Exception? privateFailure = null;
                try
                {
                    var destination = NativePackageForkCopy.Copy(fixture.Root, destinationOwner, layout, sourceLoadedPath);
                    var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
                    var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, manager.GetCurrentSolution(original.WorkspaceId), destination, properties, destinationOwner, layout);
                    using var scope = policy.Enter(capability);
                    using var ownedCopy = capability.EnterOwnedCopy(destinationOwner, layout);
                    var actualProperties = new Dictionary<string, string>(properties);
                    if (mismatch == "globals")
                        actualProperties["Configuration"] = "Different";
                    await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => loader.LoadWithPropertiesAsync(mismatch == "path" ? Path.Combine(fixture.Root, "unissued.csproj") : destination, actualProperties, SupplementalPackageReadAuthority.None, capability, CancellationToken.None));
                    Assert.HasCount(1, manager.ListWorkspaces());
                    Assert.AreEqual(original.WorkspaceId, manager.ListWorkspaces().Single().WorkspaceId);
                }
                catch (Exception failure)
                {
                    privateFailure = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure, destinationOwner);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure14 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure14, manager);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure13 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure13, fixture);
        }
    }

    [TestMethod]
    public async Task CapacityOne_RefusesForkWithoutEvictingOriginalOrConsumingPreview()
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
        Exception? resourceFailure15 = null;
        try
        {
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            var previews = new PreviewStore();
            var manager = CreateManager(policy, new FileWatcherService(NullLogger<FileWatcherService>.Instance), 1, previews);
            Exception? resourceFailure16 = null;
            try
            {
                var original = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var solution = manager.GetCurrentSolution(original.WorkspaceId);
                var version = manager.GetCurrentVersion(original.WorkspaceId);
                var token = previews.Store(original.WorkspaceId, solution, solution, version, "capacity authority", [], RoslynMcp.Core.Models.PreviewKind.Unspecified);
                var loader = (IWorkspaceEvaluationLoader)manager;
                var sourceLoadedPath = manager.GetStatus(original.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, sourceLoadedPath, solution, policy, CancellationToken.None);
                var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "private"));
                Exception? privateFailure = null;
                try
                {
                    var destination = NativePackageForkCopy.Copy(fixture.Root, destinationOwner, layout, sourceLoadedPath);
                    var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
                    var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, solution, destination, properties, destinationOwner, layout);
                    using var scope = policy.Enter(capability);
                    using var ownedCopy = capability.EnterOwnedCopy(destinationOwner, layout);
                    await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.LoadWithPropertiesAsync(destination, properties, loader.CapturePackageReadAuthority(original.WorkspaceId, solution), capability, CancellationToken.None));
                    Assert.AreSame(solution, manager.GetCurrentSolution(original.WorkspaceId));
                    Assert.AreEqual(version, manager.GetCurrentVersion(original.WorkspaceId));
                    Assert.IsNotNull(previews.Retrieve(token));
                }
                catch (Exception failure)
                {
                    privateFailure = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure, destinationOwner);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure16 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure16, manager);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure15 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure15, fixture);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedPostRegistration_RemovesOnlyItsSessionAndReturnsCapacity(bool closeOriginal)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0", new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
        Exception? resourceFailure17 = null;
        try
        {
            var policy = new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy());
            var watcher = new CallbackWatcher();
            var manager = CreateManager(policy, watcher, 2, new PreviewStore());
            IDisposable? managerFallback = manager;
            Exception? resourceFailure18 = null;
            try
            {
                var original = await manager.LoadAsync(Path.Combine(fixture.Root, "Owner.csproj"), fixture.LoadProperties, CancellationToken.None);
                var loader = (IWorkspaceEvaluationLoader)manager;
                var source = manager.GetCurrentSolution(original.WorkspaceId);
                var sourceLoadedPath = manager.GetStatus(original.WorkspaceId).LoadedPath ?? throw new AssertFailedException("The source must retain its exact loaded entry.");
                var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, sourceLoadedPath, source, policy, CancellationToken.None);
                var destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root, "private"));
                Exception? privateFailure = null;
                try
                {
                    var destination = NativePackageForkCopy.Copy(fixture.Root, destinationOwner, layout, sourceLoadedPath);
                    var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
                    var packages = loader.CapturePackageReadAuthority(original.WorkspaceId, source);
                    var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, source, destination, properties, destinationOwner, layout);
                    var injected = new IOException("Owned watcher registration failed.");
                    string? attemptedId = null;
                    watcher.AfterWatch = id =>
                    {
                        attemptedId = id;
                        Assert.IsTrue(manager.HasSession(id), "The real native result must be registered before the injected boundary.");
                        if (closeOriginal)
                            Assert.IsTrue(manager.Close(original.WorkspaceId));
                        else
                            throw injected;
                    };
                    using (policy.Enter(capability))
                    using (capability.EnterOwnedCopy(destinationOwner, layout))
                    {
                        if (closeOriginal)
                            await Assert.ThrowsExactlyAsync<WorkspaceMutationOwnershipException>(() => loader.LoadWithPropertiesAsync(destination, properties, packages, capability, CancellationToken.None));
                        else
                            Assert.AreSame(injected, await Assert.ThrowsExactlyAsync<IOException>(() => loader.LoadWithPropertiesAsync(destination, properties, packages, capability, CancellationToken.None)));
                    }

                    Assert.IsNotNull(attemptedId);
                    Assert.IsFalse(manager.HasSession(attemptedId));
                    Assert.IsTrue(watcher.Unwatched.Contains(attemptedId));
                    Assert.AreEqual(closeOriginal ? 0 : 1, manager.ListWorkspaces().Count);
                    watcher.AfterWatch = null;
                    // A real subsequent load proves the failed registration returned its capacity slot.
                    var subsequent = await manager.LoadAsync(destination, properties, CancellationToken.None);
                    Assert.IsTrue(subsequent.IsLoaded);
                    Assert.IsTrue(manager.HasSession(subsequent.WorkspaceId));
                    if (!closeOriginal)
                        Assert.AreSame(source, manager.GetCurrentSolution(original.WorkspaceId));
                }
                catch (Exception failure)
                {
                    privateFailure = failure;
                    throw;
                }
                finally
                {
                    var managerToDispose = managerFallback;
                    managerFallback = null;
                    OwnedResourceCleanup.DisposePreservingFailure("Private invocation namespace cleanup failed.", privateFailure,
                        managerToDispose, destinationOwner);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure18 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure18, managerFallback);
            }
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure17 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native ownership resource cleanup failed.", resourceFailure17, fixture);
        }
    }

    private static WorkspaceManager CreateManager(ForkInvocationPathPolicy policy, IFileWatcherService watcher, int capacity, PreviewStore previews)
    {
        try
        {
            return new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, previews, watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = capacity });
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Fork authority manager construction cleanup failed.", failure, watcher);
            throw;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PostRegistrationFinalGrant_CannotPublishChangedPrivateLoadInputs(bool certified)
    {
        var privateRoot = Path.Combine(TestTempRoot.Current, "outer-private-" + Guid.NewGuid().ToString("N"));
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        PhysicalDirectoryScope.OwnedRoot? destinationOwner = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Closure", "1.0.0",
                new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class PackageInput { }"u8.ToArray() });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var authored = Path.Combine(fixture.Root, "Owned.cs");
            const string originalSource = "public sealed class Owned { }";
            await File.WriteAllTextAsync(authored, originalSource);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "outer.rsp"), "@nested.rsp\n");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "nested.rsp"), "/ruleset:root.ruleset\n");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "root.ruleset"),
                "<RuleSet Name=\"Root\" ToolsVersion=\"15.0\"><Include Path=\"included.ruleset\" Action=\"Default\" /></RuleSet>");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "included.ruleset"),
                "<RuleSet Name=\"Included\" ToolsVersion=\"15.0\" />");
            var definition = System.Xml.Linq.XDocument.Load(project);
            definition.Root!.Element("PropertyGroup")!.Add(new System.Xml.Linq.XElement("CompilerResponseFile", "outer.rsp"));
            definition.Root.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("None",
                new System.Xml.Linq.XAttribute("Remove", "*.rsp;*.ruleset"))));
            await File.WriteAllTextAsync(project, definition.ToString());
            var raw = new CallbackPolicy();
            var policy = new ForkInvocationPathPolicy(raw);
            var watcher = new CallbackWatcher();
            manager = CreateManager(policy, watcher, 2, new PreviewStore());
            var original = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            var source = manager.GetCurrentSolution(original.WorkspaceId);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(source).Availability);
            var loader = (IWorkspaceEvaluationLoader)manager;
            var layout = await PhysicalGraphLayout.CreateAsync(original.WorkspaceId, project, source, policy, CancellationToken.None);
            destinationOwner = PhysicalDirectoryScope.CreateOwnedRoot(privateRoot);
            var destination = NativePackageForkCopy.Copy(fixture.Root, destinationOwner, layout, project);
            var destinationDirectory = Path.GetDirectoryName(destination)!;
            foreach (var name in new[] { "Owned.cs", "outer.rsp", "nested.rsp", "root.ruleset", "included.ruleset" })
                File.Copy(Path.Combine(fixture.Root, name), Path.Combine(destinationDirectory, name));
            var properties = OwnedNativeLoadContext.Properties(destination, new Dictionary<string, string>());
            if (certified)
            {
                var restored = await new DotnetCommandRunner().RunAsync(destinationDirectory, destination,
                    MsBuildCommandProperties.Append(["restore", destination, "--nologo", "--source", Path.Combine(fixture.Root, "feed"),
                        "--packages", fixture.Cache], properties), CancellationToken.None);
                Assert.IsTrue(restored.Succeeded, restored.StdErr + restored.StdOut);
            }
            else Assert.IsFalse(File.Exists(Path.Combine(destinationDirectory, "obj", "project.assets.json")));
            var packages = loader.CapturePackageReadAuthority(original.WorkspaceId, source);
            var capability = loader.CaptureForkInvocationAuthority(original.WorkspaceId, source, destination, properties, destinationOwner, layout);
            var changedPath = Path.Combine(destinationDirectory, certified ? "included.ruleset" : "Owned.cs");
            string? attemptedId = null;
            var mutations = 0;
            watcher.AfterWatch = id =>
            {
                attemptedId = id;
                Assert.AreNotEqual(original.WorkspaceId, id);
                Assert.IsTrue(manager.HasSession(id), "The actual private result must be registered before the final policy callback is armed.");
                Assert.AreEqual(destination, manager.GetStatus(id).LoadedPath);
                var published = manager.GetCurrentSolution(id);
                Assert.IsTrue(NativeCompilerContextCertificate.TryGet(published, out var context));
                Assert.AreEqual(certified ? NativeCompilerContextAvailability.Certified : NativeCompilerContextAvailability.InvalidRestoreAssets,
                    context!.Availability);
                if (certified)
                {
                    CollectionAssert.Contains(context.FinalRestoreGrantPaths(published).ToArray(), changedPath);
                    var nativeFiles = EvaluatedBuildGraph.Source(context.NativeSolution, CancellationToken.None).Nodes.SelectMany(node => node.DocumentFiles
                        .Concat(node.ArtifactFiles).Concat(node.PathEvidence.InputFiles)).ToHashSet(FileSystemPath.Comparer);
                    Assert.IsFalse(nativeFiles.Contains(changedPath), "The private ruleset must rely on its hidden parser receipt.");
                }
                else Assert.IsTrue(published.Projects.SelectMany(owner => owner.Documents)
                    .Any(document => FileSystemPath.Comparer.Equals(document.FilePath, changedPath)));
                raw.OnValidate = async sourceId =>
                {
                    Assert.AreEqual(original.WorkspaceId, sourceId, "The real fork policy must refresh the original source scope.");
                    if (mutations != 0) return;
                    mutations++;
                    await File.AppendAllTextAsync(changedPath, certified ? "<!-- changed at final fork grant -->" : "\n// changed at final fork grant\n");
                };
            };
            Exception? refused = null;
            using (policy.Enter(capability))
            using (capability.EnterOwnedCopy(destinationOwner, layout))
            {
                try { await loader.LoadWithPropertiesAsync(destination, properties, packages, capability, CancellationToken.None); }
                catch (Exception failure) { refused = failure; }
            }
            Assert.IsNotNull(attemptedId, "The actual allocated private native result must reach registration.");
            Assert.AreEqual(1, mutations, "The post-registration fork policy callback must actually mutate private inputs.");
            StringAssert.Contains(await File.ReadAllTextAsync(changedPath), "changed at final fork grant");
            Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType());
            Assert.IsFalse(manager.HasSession(attemptedId), "A changed private load must not retain its newly registered session.");
            Assert.IsTrue(watcher.Unwatched.Contains(attemptedId));
            Assert.AreSame(source, manager.GetCurrentSolution(original.WorkspaceId));
            Assert.AreEqual(originalSource, await File.ReadAllTextAsync(authored));
            Assert.HasCount(1, manager.ListWorkspaces());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Final private load publication cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => destinationOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(privateRoot)),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private static ServiceProvider CreateProvider(ForkInvocationPathPolicy policy, IDotnetCommandRunner runner)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(policy);
        services.AddSingleton<IWorkspacePathPolicy>(policy);
        services.AddSingleton(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
        services.AddSingleton(new ExecutionGateOptions { RateLimitMaxRequests = int.MaxValue });
        services.AddRoslynServices();
        services.AddSingleton(runner);
        return services.BuildServiceProvider();
    }

    private sealed class CountingCommandRunner : IDotnetCommandRunner
    {
        private readonly DotnetCommandRunner _inner = new();
        private int _count;
        internal int Count => Volatile.Read(ref _count);
        public Task<CommandExecutionDto> RunAsync(string directory, string target, IReadOnlyList<string> arguments, CancellationToken ct)
            => RunAsync(directory, target, arguments, null, ct);
        public Task<CommandExecutionDto> RunAsync(string directory, string target, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? patterns, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            return _inner.RunAsync(directory, target, arguments, patterns, ct);
        }
        public Task<CommandExecutionDto> RunAsync(string directory, string target, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? patterns, string executable, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            return _inner.RunAsync(directory, target, arguments, patterns, executable, ct);
        }
    }

    private sealed class CallbackWatcher : IFileWatcherService
    {
        private readonly FileWatcherService _inner = new(NullLogger<FileWatcherService>.Instance);
        internal Action<string>? AfterWatch { get; set; }
        internal HashSet<string> Unwatched { get; } = [];
        public event Action<string>? WorkspaceRootMissing { add => _inner.WorkspaceRootMissing += value; remove => _inner.WorkspaceRootMissing -= value; }
        public void Watch(string id, string path) { _inner.Watch(id, path); AfterWatch?.Invoke(id); }
        public void Unwatch(string id) { Unwatched.Add(id); _inner.Unwatch(id); }
        public bool IsStale(string id) => _inner.IsStale(id);
        public Task WaitForStaleAsync(string id, CancellationToken ct) => _inner.WaitForStaleAsync(id, ct);
        public string? GetStaleReason(string id) => _inner.GetStaleReason(id);
        public void MarkStale(string id, string reason) => _inner.MarkStale(id, reason);
        public void ClearStale(string id) => _inner.ClearStale(id);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class CallbackPolicy : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal Func<string, Task>? OnValidate { get; set; }
        public async Task ValidateAsync(string id, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (OnValidate is { } callback) await callback(id);
            await _owned.ValidateAsync(id, paths, ct);
        }
    }

    private sealed class OriginalOnlyPolicy(string root) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal string? SourceId { get; set; }
        internal System.Collections.Concurrent.ConcurrentQueue<string> Scopes { get; } = new();
        public Task ValidateAsync(string id, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (SourceId is { } source && paths.Any(path => FileSystemPath.Comparer.Equals(path, root) || FileSystemPath.IsStrictDescendant(root, path)))
            {
                Scopes.Enqueue(id);
                if (id != source) throw new UnauthorizedAccessException("The original workspace owns the project and cache grant.");
            }
            return _owned.ValidateAsync(id, paths, ct);
        }
    }
}
