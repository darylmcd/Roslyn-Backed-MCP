using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeReferenceFreePublicationTests
{
    [TestMethod]
    [DataRow("stable")]
    [DataRow("source")]
    [DataRow("followup-source")]
    [DataRow("new-compile")]
    [DataRow("revoked-followup-grant")]
    [DataRow("cancelled")]
    public async Task ReferenceFreeNormalization_PublishesOnlyAfterCompleteCurrentBoundary(string scenario)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceFreePublicationTests), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "NoProjectReferences.csproj");
        var source = Path.Combine(root, "Entry.cs");
        var added = Path.Combine(root, "Added.cs");
        var invocationMarker = Path.Combine(root, "obj", "CompilerInvocations.txt");
        var policy = new PublicationPolicy(source, added, scenario);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(source, "public sealed class Entry { }");
            await File.WriteAllTextAsync(path, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <Target Name="ObserveActualCompilerInvocation" BeforeTargets="CoreCompile">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/CompilerInvocations.txt" Lines="compiler-invoked"
                                      Overwrite="true" WriteOnlyWhenDifferent="false" />
                  </Target>
                </Project>
                """);
            var properties = OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>());
            var restore = await new DotnetCommandRunner().RunAsync(root, path,
                MsBuildCommandProperties.Append(["restore", path, "--nologo"], properties), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("reference-free-publication", path, properties,
                policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance,
                SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            Exception? loadFailure = null;
            CancellationTokenSource? cancellation = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                Assert.HasCount(1, loaded.Context.NativeSolution.Projects.ToArray());
                Assert.IsFalse(loaded.Context.NativeSolution.Projects.Any(project => project.ProjectReferences.Any()),
                    "This fixture must need no compiler-reference producer during normalization.");
                Assert.IsTrue(File.Exists(invocationMarker), "The real native compiler target must exercise the invocation oracle during owned load.");
                var invocationBytes = await File.ReadAllBytesAsync(invocationMarker);
                File.SetLastWriteTimeUtc(invocationMarker, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                var invocationStamp = File.GetLastWriteTimeUtc(invocationMarker);
                var qualified = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(
                    "reference-free-publication", loaded.Context, policy, CancellationToken.None);
                Assert.AreSame(loaded.Context, NativeCompilerContextCertificate.Require(qualified.Solution));
                var completeGrants = new List<string[]>();
                policy.ObserveRetainedGrant = paths => completeGrants.Add(paths.ToArray());
                try
                {
                    await loaded.Context.AssertCurrentAsync(qualified.Solution, "reference-free-publication", policy, CancellationToken.None);
                }
                finally { policy.ObserveRetainedGrant = null; }
                Assert.HasCount(1, completeGrants, "The actual policy-only current boundary must issue one complete retained grant.");
                CollectionAssert.Contains(completeGrants[0], path);
                CollectionAssert.Contains(completeGrants[0], source);
                policy.Arm(completeGrants[0]);
                cancellation = new CancellationTokenSource();
                if (scenario == "cancelled") cancellation.Cancel();
                async Task NormalizeAsync() => _ = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(
                    "reference-free-publication", loaded.Context, policy, cancellation.Token);
                if (scenario == "stable")
                {
                    var projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(
                        "reference-free-publication", loaded.Context, policy, CancellationToken.None);
                    Assert.AreSame(loaded.Context, NativeCompilerContextCertificate.Require(projection.Solution));
                    var repeated = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(
                        "reference-free-publication", loaded.Context, policy, CancellationToken.None);
                    Assert.AreSame(projection, repeated, "Repeated current validation must retain the exact issued projection.");
                    Assert.IsTrue(policy.SourceBoundaryChecks > 0, "Current source grants must still be checked.");
                    CollectionAssert.AreEqual(invocationBytes, await File.ReadAllBytesAsync(invocationMarker));
                    Assert.AreEqual(invocationStamp, File.GetLastWriteTimeUtc(invocationMarker),
                        "Current validation must not execute another native compiler producer.");
                    var nativeOwner = loaded.Context.NativeSolution.Projects.Single();
                    var node = EvaluatedBuildGraph.Resolve(nativeOwner, EvaluatedBuildGraph.Source(loaded.Context.NativeSolution, CancellationToken.None));
                    _ = await NativeCompilerTaskInputs.ReadRawAsync(node.ProjectPath, node.Globals, node.TargetFramework,
                        node.TaskRegistrations.ToolsVersion, node.CompilerReferences.NativeCompileTargets,
                        NativeCompilerTaskInputs.CapturePurpose.FinalCompiler, CancellationToken.None);
                    CollectionAssert.AreEqual(invocationBytes, await File.ReadAllBytesAsync(invocationMarker));
                    Assert.AreNotEqual(invocationStamp, File.GetLastWriteTimeUtc(invocationMarker),
                        "The independent actual compiler producer must touch the oracle, proving it detects reexecution.");
                    var project = projection.Solution.Projects.Single();
                    var compilation = await project.GetCompilationAsync();
                    Assert.IsNotNull(compilation);
                    Assert.IsNotNull(compilation.GetTypeByMetadataName("Entry"));
                    Assert.IsFalse(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
                    var reference = project.MetadataReferences.OfType<PortableExecutableReference>()
                        .Single(item => Path.GetFileName(item.FilePath) == "System.Runtime.dll");
                    var images = await loaded.Context.ReadAdmittedImagesAsync(projection.Solution, project.Id, reference,
                        "reference-free-publication", policy, CancellationToken.None);
                    Assert.HasCount(1, images);
                    Assert.IsFalse(images[0].Image.IsEmpty, "The resulting certificate must retain usable admitted image authority.");
                    await File.AppendAllTextAsync(source, "\n// changed after publication\n");
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(NormalizeAsync,
                        "Repeated normalization must revalidate even when its projection is already issued.");
                }
                else if (scenario == "cancelled")
                {
                    var cancelled = await Assert.ThrowsExactlyAsync<TaskCanceledException>(NormalizeAsync);
                    Assert.AreEqual(cancellation.Token, cancelled.CancellationToken);
                }
                else
                {
                    Exception? refusal = null;
                    try { await NormalizeAsync(); }
                    catch (Exception failure) { refusal = failure; }
                    Assert.IsTrue(policy.Triggered, "The retained normalization boundary must exercise the controlled mutation/refusal.");
                    if (scenario == "new-compile")
                        Assert.AreEqual("public sealed class Added { }", await File.ReadAllTextAsync(added));
                    else if (scenario != "revoked-followup-grant")
                        Assert.AreEqual("public sealed class Entry { }\n// mutated at normalization authority boundary\n",
                            await File.ReadAllTextAsync(source));
                    var expectedType = scenario == "revoked-followup-grant"
                        ? typeof(UnauthorizedAccessException)
                        : typeof(PublicInvalidOperationException);
                    Assert.AreEqual(expectedType, refusal?.GetType(),
                        "The complete current boundary must refuse after its actual mutation or grant revocation.");
                }
            }
            catch (Exception failure) { loadFailure = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Reference-free native load cleanup failed.", loadFailure, cancellation, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Reference-free publication fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class PublicationPolicy(string source, string added, string scenario) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        private bool _armed;
        private HashSet<string>? _completeGrant;
        internal Action<IReadOnlyList<string>>? ObserveRetainedGrant { get; set; }
        internal int SourceBoundaryChecks { get; private set; }
        internal bool Triggered { get; private set; }
        internal void Arm(IReadOnlyList<string> completeGrant)
        {
            _completeGrant = completeGrant.ToHashSet(FileSystemPath.Comparer);
            Assert.IsTrue(_completeGrant.Contains(source), "The actual issued certificate must retain the authored source grant.");
            _armed = true;
        }

        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(workspaceId, paths, ct);
            ObserveRetainedGrant?.Invoke(paths);
            if (!_armed || !paths.Contains(source, FileSystemPath.Comparer)) return;
            SourceBoundaryChecks++;
            if (Triggered || scenario is "stable" or "cancelled") return;
            if (!_completeGrant!.SetEquals(paths)) return;
            Triggered = true;
            if (scenario == "revoked-followup-grant")
                throw new UnauthorizedAccessException("The reference-free source grant was revoked during follow-up validation.");
            if (scenario == "new-compile") await File.WriteAllTextAsync(added, "public sealed class Added { }", ct);
            else await File.AppendAllTextAsync(source, "\n// mutated at normalization authority boundary\n", ct);
        }
    }
}
