using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PreloadedAnalyzerAuthorityTests : IsolatedWorkspaceTestBase
{
    private const string WorkerFixtureRoot = "ROSLYNMCP_WORKER_GENERATOR_FIXTURE";
    private const string WorkerConstructorMarker = "ROSLYNMCP_WORKER_CONSTRUCTOR_MARKER";
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task NativeGenerator_UsesCapturedDependencyInsteadOfForeignDefaultImage()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Captured shadow executable images are the Windows analyzer contract.");
            return;
        }
        IsolatedWorkspaceScope? source = null;
        MSBuildWorkspace? workspace = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease = null;
        Exception? primary = null;
        try
        {
            source = await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
            var projectPath = source.GetPath("Owner", "Owner.csproj");
            var directory = source.GetPath("Owner", "generator");
            Directory.CreateDirectory(directory);
            var dependencyName = "AnalyzerDependency" + Guid.NewGuid().ToString("N");
            var foreignBytes = Emit(dependencyName, "public static class DependencyValue { public static string Value => \"Foreign\"; }");
            var admittedBytes = Emit(dependencyName, "public static class DependencyValue { public static string Value => \"Admitted\"; }");
            using (var foreignStream = new MemoryStream(foreignBytes))
            {
                var foreign = AssemblyLoadContext.Default.LoadFromStream(foreignStream);
                Assert.AreSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(foreign));
                Assert.AreEqual("Foreign", foreign.GetType("DependencyValue")!.GetProperty("Value")!.GetValue(null));
            }
            var generator = Emit("CatalogGenerator" + Guid.NewGuid().ToString("N"), """
                using System.Collections.Immutable;
                using System.Threading.Tasks;
                using Microsoft.CodeAnalysis;
                using Microsoft.CodeAnalysis.CodeFixes;
                [Generator]
                public sealed class CatalogGenerator : ISourceGenerator
                {
                    public void Initialize(GeneratorInitializationContext context) { }
                    public void Execute(GeneratorExecutionContext context) =>
                        context.AddSource("DependencyMarker.g.cs", "public sealed class " + DependencyValue.Value + "DependencyMarker { }");
                }
                [ExportCodeFixProvider(LanguageNames.CSharp, Name = "CatalogCodeFix")]
                [System.Composition.Shared]
                public sealed class CatalogCodeFix : CodeFixProvider
                {
                    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("OWNED001");
                    public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;
                }
                """, MetadataReference.CreateFromImage(admittedBytes),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Composition.ExportAttribute).Assembly.Location));
            await File.WriteAllBytesAsync(Path.Combine(directory, dependencyName + ".dll"), admittedBytes);
            var generatorPath = Path.Combine(directory, "CatalogGenerator.dll");
            await File.WriteAllBytesAsync(generatorPath, generator);
            var definition = XDocument.Load(projectPath);
            definition.Root!.Add(new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Include", generatorPath))));
            await File.WriteAllTextAsync(projectPath, definition.ToString());
            var policy = await NativeSdkRestoreFixture.CreatePolicyAsync(projectPath, CancellationToken.None);
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("native-dependency-pollution", projectPath,
                OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            workspace = loaded.Workspace;
            lease = loaded.Lease;
            Assert.AreEqual("Certified", loaded.Context.Availability.ToString());
            var published = (await loaded.Context.NormalizeAsync("native-dependency-pollution", policy, CancellationToken.None)).Solution;
            var snapshot = await SourceGeneratorCompilation.CreateAsync(published.Projects.Single(), CancellationToken.None);
            Assert.IsNotNull(snapshot);
            Assert.IsNotNull(snapshot.Compilation.GetTypeByMetadataName("AdmittedDependencyMarker"),
                "The actual native generator must execute the captured dependency, even when Default already has a compatible foreign image.");
            Assert.IsNull(snapshot.Compilation.GetTypeByMetadataName("ForeignDependencyMarker"));
            var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
            Assert.IsTrue(registry.GetProvidersFor("OWNED001", published).Any(provider => provider.GetType().Name == "CatalogCodeFix"),
                "An actual owned provider must share the exact host Workspaces contract and remain discoverable.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native dependency pollution cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                () => source is null ? ValueTask.CompletedTask : source.DisposeAsync());
        }
    }

    private static byte[] Emit(string name, string text, params MetadataReference[] extra)
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(text)],
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location) }.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        Assert.IsTrue(emit.Success, string.Join("; ", emit.Diagnostics));
        return bytes.ToArray();
    }

    [TestMethod]
    public Task NativeGenerator_PrivateReferenceOwnsMetadata() => AssertPrivateReferenceAsync(false, null, false, false);

    [TestMethod]
    public Task NativeGenerator_ConstructorCanResolveAdmittedDependencyOnWorker() => RunWorkerConstructorAsync(false);

    [TestMethod]
    public Task NativeGenerator_RetirementDrainsAdmittedConstructorWorker() => RunWorkerConstructorAsync(true);

    private async Task RunWorkerConstructorAsync(bool retire)
    {
        var method = retire ? nameof(NativeGenerator_RetirementDrainsAdmittedConstructorWorker)
            : nameof(NativeGenerator_ConstructorCanResolveAdmittedDependencyOnWorker);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Owned analyzer executable images are the Windows shadow contract.");
            return;
        }
        if (Environment.GetEnvironmentVariable(WorkerFixtureRoot) is { } inherited)
        {
            await NativeConditionProcessTest.RunAsync(typeof(PreloadedAnalyzerAuthorityTests),
                method,
                new Dictionary<string, string?> { [WorkerFixtureRoot] = inherited,
                    [WorkerConstructorMarker] = Environment.GetEnvironmentVariable(WorkerConstructorMarker) },
                () => AssertPrivateReferenceAsync(false, null, true, retire));
            return;
        }
        IsolatedWorkspaceScope? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
            var marker = fixture.GetPath("constructor-entered.marker");
            try
            {
                await NativeConditionProcessTest.RunAsync(typeof(PreloadedAnalyzerAuthorityTests),
                    method,
                    new Dictionary<string, string?> { [WorkerFixtureRoot] = fixture.RootPath, [WorkerConstructorMarker] = marker },
                    () => AssertPrivateReferenceAsync(false, null, true, retire));
            }
            catch (Exception failure)
            {
                string premise;
                try { premise = File.Exists(marker) ? await File.ReadAllTextAsync(marker) : "not reached"; }
                catch (Exception receiptFailure) { throw new AggregateException("Owned native child and constructor receipt failed.", failure, receiptFailure); }
                throw new AggregateException("Owned native child failed; actual generator constructor/worker receipt: " + premise, failure);
            }
            Assert.IsTrue(File.Exists(marker), "The actual constructor must run before its admitted worker dependency is resolved.");
            var receipt = (await File.ReadAllTextAsync(marker)).Split(';');
            Assert.AreNotEqual(receipt.Single(item => item.StartsWith("ctor=", StringComparison.Ordinal))[5..],
                receipt.Single(item => item.StartsWith("worker=", StringComparison.Ordinal))[7..]);
            CollectionAssert.Contains(receipt, "loaded", "The worker must complete an explicit demand load through the generator's actual ALC.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Worker native fixture cleanup failed.", primary,
                () => fixture is null ? ValueTask.CompletedTask : fixture.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task NativeGenerator_PrivateReferenceSurvivesInflightNativeLoad()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Owned analyzer executable images are the Windows shadow contract.");
            return;
        }
        const string key = "ROSLYNMCP_INFLIGHT_GENERATOR_IMAGES";
        if (Environment.GetEnvironmentVariable(key) is { } inherited)
        {
            await AssertPrivateReferenceAsync(true, inherited, false, false);
            return;
        }
        var directory = Path.Combine(TestTempRoot.Current, "InflightNativeImages", Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            await NativeConditionProcessTest.RunAsync(typeof(PreloadedAnalyzerAuthorityTests),
                nameof(NativeGenerator_PrivateReferenceSurvivesInflightNativeLoad),
                new Dictionary<string, string?> { [key] = directory }, () => AssertPrivateReferenceAsync(true, directory, false, false));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Parent-owned native image cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }

    private static async Task AssertPrivateReferenceAsync(bool inflight, string? externalImages, bool workerConstructor, bool retire)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Owned analyzer executable images are the Windows shadow contract.");
            return;
        }
        IsolatedWorkspaceScope? source = null;
        EventWaitHandle? entered = null;
        EventWaitHandle? go = null;
        InflightNativeGeneratorLoader? loader = null;
        MSBuildWorkspace? ownedWorkspace = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? ownedLease = null;
        Exception? primary = null;
        try
        {
            source = workerConstructor ? null : await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
            var root = source?.RootPath ?? Environment.GetEnvironmentVariable(WorkerFixtureRoot)
                ?? throw new AssertFailedException("The worker child requires its parent-owned fixture root.");
            string GetPath(params string[] segments) => Path.Combine([root, .. segments]);
            var projectPath = GetPath("Owner", "Owner.csproj");
            var assemblyName = "PrivateGenerator" + Guid.NewGuid().ToString("N");
            var generatorPath = externalImages is null ? GetPath("Owner", assemblyName + ".dll") : Path.Combine(externalImages, assemblyName + ".dll");
            var dependencyName = "ConstructorDependency" + Guid.NewGuid().ToString("N");
            var enteredName = "RoslynAnalyzerEntered" + Guid.NewGuid().ToString("N");
            var goName = "RoslynAnalyzerGo" + Guid.NewGuid().ToString("N");
            entered = retire ? new EventWaitHandle(false, EventResetMode.ManualReset, enteredName) : null;
            go = retire ? new EventWaitHandle(false, EventResetMode.ManualReset, goName) : null;
            string retirementBarrier = !retire ? "" : $$"""
                        using var entered = System.Threading.EventWaitHandle.OpenExisting("{{enteredName}}");
                        using var go = System.Threading.EventWaitHandle.OpenExisting("{{goName}}");
                        entered.Set();
                        if (!go.WaitOne(System.TimeSpan.FromSeconds(20))) throw new System.TimeoutException();
                """;

            string Constructor(string type) => !workerConstructor ? "" : $$"""
                public {{type}}()
                {
                    var marker = System.Environment.GetEnvironmentVariable("{{WorkerConstructorMarker}}");
                    System.IO.File.WriteAllText(marker, "ctor=" + System.Environment.CurrentManagedThreadId);
                    var value = System.Threading.Tasks.Task.Factory.StartNew(() =>
                    {
                        System.IO.File.AppendAllText(marker, ";worker=" + System.Environment.CurrentManagedThreadId);
                        {{retirementBarrier}}
                        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof({{type}}).Assembly);
                        var dependency = context.LoadFromAssemblyName(new System.Reflection.AssemblyName("{{dependencyName}}"));
                        System.IO.File.AppendAllText(marker, ";loaded");
                        return dependency.GetType("ConstructorDependency").GetProperty("Value").GetValue(null) as string;
                    }, System.Threading.CancellationToken.None, System.Threading.Tasks.TaskCreationOptions.LongRunning,
                        System.Threading.Tasks.TaskScheduler.Default).GetAwaiter().GetResult();
                    if (value != "Worker") throw new System.InvalidOperationException();
                }
                """;
            string Generator(string type, string marker) => $$"""
                using Microsoft.CodeAnalysis;
                [Generator] public sealed class {{type}} : ISourceGenerator
                {
                    {{Constructor(type)}}
                    public void Initialize(GeneratorInitializationContext context) { }
                    public void Execute(GeneratorExecutionContext context) =>
                        context.AddSource("OwnedMarker.g.cs", "public sealed class {{marker}} { }");
                }
                """;
            MetadataReference[] extra = [];
            if (workerConstructor)
            {
                var dependency = Emit(dependencyName, "public static class ConstructorDependency { public static string Value => \"Worker\"; }");
                await File.WriteAllBytesAsync(GetPath("Owner", dependencyName + ".dll"), dependency);
                extra = [MetadataReference.CreateFromImage(dependency),
                    MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.IO.FileSystem.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.Loader.dll"))];
            }
            var original = Emit(assemblyName, Generator("OwnedGenerator", "OwnedImageMarker"), extra);
            await File.WriteAllBytesAsync(generatorPath, original);
            var definition = XDocument.Load(projectPath);
            definition.Root!.Add(new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Include", generatorPath))));
            await File.WriteAllTextAsync(projectPath, definition.ToString());
            IWorkspacePathPolicy policy = await NativeSdkRestoreFixture.CreatePolicyAsync(projectPath, CancellationToken.None);
            if (externalImages is not null) policy = new ExternalNativeImagePolicy(policy, externalImages);
            if (workerConstructor) policy = new ExternalNativeImagePolicy(policy, root);
            loader = new InflightNativeGeneratorLoader(generatorPath, assemblyName, inflight);
            var loaded = await loader.CreateAndOpenAsync("private-analyzer-reference", projectPath,
                OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), policy,
                new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            ownedWorkspace = loaded.Workspace;
            ownedLease = loaded.Lease;
            var published = (await loaded.Context.NormalizeAsync("private-analyzer-reference", policy, CancellationToken.None)).Solution;
            if (inflight)
            {
                await loader.ReleaseAndJoinAsync();
                Assert.IsNotNull(loader.OriginalReference);
                Assert.IsNotEmpty(loader.OriginalReference.GetGenerators(LanguageNames.CSharp), "The real original native load must finish after output binding.");
                Assert.IsFalse(published.Projects.Single().AnalyzerReferences
                    .Any(reference => ReferenceEquals(reference, loader.OriginalReference)),
                    "The published output must not share a native reference whose old loader can finish after binding.");
            }
            else if (!workerConstructor)
            {
                await File.WriteAllBytesAsync(generatorPath, Emit(assemblyName, Generator("ChangedGenerator", "ChangedImageMarker")));
            }
            if (retire)
            {
                var owned = published.Projects.Single().AnalyzerReferences.Single(item => item.FullPath == generatorPath);
                ownedLease = null;
                await AssertRetirementAsync(owned, loaded.Lease, entered!, go!);
                return;
            }
            var snapshot = await SourceGeneratorCompilation.CreateAsync(published.Projects.Single(), CancellationToken.None);
            Assert.IsNotNull(snapshot);
            Assert.IsNotNull(snapshot.Compilation.GetTypeByMetadataName("OwnedImageMarker"),
                "Both native metadata discovery and executable loading must use the held original shadow image.");
            Assert.IsNull(snapshot.Compilation.GetTypeByMetadataName("ChangedImageMarker"));
            if (!inflight)
            {
                var reference = published.Projects.Single().AnalyzerReferences.Single(item => item.FullPath == generatorPath);
                ownedLease = null;
                loaded.Lease.Dispose();
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => _ = reference.Id);
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => _ = reference.Display);
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => reference.GetGenerators(LanguageNames.CSharp));
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => reference.GetGeneratorsForAllLanguages());
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => reference.GetAnalyzers(LanguageNames.CSharp));
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => reference.GetAnalyzersForAllLanguages());
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Private analyzer reference cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => go?.Set()),
                () => loader is null ? ValueTask.CompletedTask : new ValueTask(loader.ReleaseAndJoinAsync()),
                CleanupFailureCollector.FromAction(() => ownedWorkspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => ownedLease?.Dispose()),
                CleanupFailureCollector.FromAction(() => loader?.Dispose()),
                CleanupFailureCollector.FromAction(() => go?.Dispose()),
                CleanupFailureCollector.FromAction(() => entered?.Dispose()),
                () => source is null ? ValueTask.CompletedTask : source.DisposeAsync());
        }
    }

    private static async Task AssertRetirementAsync(AnalyzerReference reference,
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease lease, EventWaitHandle entered, EventWaitHandle go)
    {
        Task<ImmutableArray<ISourceGenerator>>? admitted = null;
        CancellationTokenSource? deadline = null;
        Task? retirement = null;
        Task<bool>? probe = null;
        Exception? primary = null;
        try
        {
            deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            admitted = Task.Run(() => reference.GetGenerators(LanguageNames.CSharp));
            Assert.IsTrue(await Task.Run(() => entered.WaitOne(TimeSpan.FromSeconds(10))),
                "The actual owned constructor worker must reach its demand-load barrier.");
            retirement = Task.Run(lease.Dispose);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                probe = Task.Run(() =>
                {
                    try { _ = reference.Id; return false; }
                    catch (PublicInvalidOperationException) { return true; }
                });
                if (await probe.WaitAsync(deadline.Token)) break;
                await Task.Delay(10, deadline.Token);
            }
            Assert.IsFalse(retirement.IsCompleted, "Retirement must drain the admitted native constructor before releasing its held images.");
            go.Set();
            Assert.IsNotEmpty(await admitted.WaitAsync(deadline.Token));
            await retirement.WaitAsync(deadline.Token);
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => _ = reference.Id);
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => reference.GetGenerators(LanguageNames.CSharp));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Admitted native constructor retirement cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => go.Set()),
                () => admitted is null ? ValueTask.CompletedTask : new ValueTask(admitted),
                () => retirement is null ? ValueTask.CompletedTask : new ValueTask(retirement),
                () => probe is null ? ValueTask.CompletedTask : new ValueTask(probe),
                CleanupFailureCollector.FromAction(() => { if (retirement is null) lease.Dispose(); }),
                CleanupFailureCollector.FromAction(() => deadline?.Dispose()));
        }
    }

    private sealed class ExternalNativeImagePolicy(IWorkspacePathPolicy native, string images) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _images = new([images]);
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            var external = paths.Where(path => FileSystemPath.Comparer.Equals(path, images) || FileSystemPath.IsStrictDescendant(images, path)).ToArray();
            await _images.ValidateAsync(workspaceId, external, ct);
            await native.ValidateAsync(workspaceId, paths.Except(external, FileSystemPath.Comparer).ToArray(), ct);
        }
    }

    private sealed class InflightNativeGeneratorLoader(string generatorPath, string assemblyName, bool inflight) : WorkspaceSessionLoader, IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private Task? _nativeLoad;
        private AssemblyLoadEventHandler? _handler;
        internal AnalyzerFileReference? OriginalReference { get; private set; }

        protected override async Task OpenNativeAsync(MSBuildWorkspace workspace, string path, CancellationToken ct)
        {
            await base.OpenNativeAsync(workspace, path, ct);
            OriginalReference = workspace.CurrentSolution.Projects.Single().AnalyzerReferences.OfType<AnalyzerFileReference>()
                .Single(reference => FileSystemPath.Comparer.Equals(reference.FullPath, generatorPath));
            if (!inflight) return;
            _handler = (_, args) =>
            {
                if (args.LoadedAssembly.GetName().Name != assemblyName) return;
                _entered.TrySetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Native generator barrier was not released.");
            };
            AppDomain.CurrentDomain.AssemblyLoad += _handler;
            _nativeLoad = Task.Run(() => OriginalReference.GetGenerators(LanguageNames.CSharp), ct);
            await _entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }

        internal async Task ReleaseAndJoinAsync()
        {
            _release.Set();
            if (_nativeLoad is not null) await _nativeLoad;
        }
        public void Dispose()
        {
            _release.Set();
            if (_handler is not null) AppDomain.CurrentDomain.AssemblyLoad -= _handler;
            _release.Dispose();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeGeneratorReload_PreservesSemanticsWithoutCertifyingForeignLoadedImages(bool preload)
    {
        IsolatedWorkspaceScope? source = null;
        Exception? fixturePrimary = null;
        try
        {
            source = await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
            var projectPath = source.GetPath("Owner", "Owner.csproj");
            await File.WriteAllTextAsync(source.GetPath("Owner", "RegexProbe.cs"), """
                using System.Text.RegularExpressions;
                namespace Owner;
                public static partial class RegexProbe
                {
                    [GeneratedRegex("^a+$")]
                    public static partial Regex Pattern();
                }
                """);
            var policy = await NativeSdkRestoreFixture.CreatePolicyAsync(projectPath, CancellationToken.None);
            var statuses = new List<string>();
            IReadOnlyList<AnalyzerFileReference>? previousReferences = null;
            for (var epoch = 0; epoch != 2; epoch++)
            {
                var loader = new EagerNativeGeneratorLoader(preload);
                var loaded = await loader.CreateAndOpenAsync("native-generator-" + epoch, projectPath,
                    OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), policy,
                    new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                Exception? primary = null;
                try
                {
                    var native = loaded.Workspace.CurrentSolution.Projects.Single();
                    var references = native.AnalyzerReferences.OfType<AnalyzerFileReference>().ToArray();
                    Assert.IsNotEmpty(references, "The installed SDK must supply actual native generator references.");
                    if (previousReferences is not null)
                        Assert.IsTrue(references.All(reference => !previousReferences.Any(previous => ReferenceEquals(previous, reference))),
                            "A real fresh native workspace must not inherit the previous AnalyzerFileReference object.");
                    previousReferences = references;
                    if (preload)
                    {
                        Assert.IsNotEmpty(loader.PreloadedReferences, "The actual generator invocation must materialize native lazy assemblies before isolation.");
                        Assert.IsTrue(loader.PreloadedReferences.All(reference => references.Any(current => ReferenceEquals(reference, current))),
                            "Isolation must retain the exact native references whose generator assemblies were already loaded.");
                    }
                    var published = (await loaded.Context.NormalizeAsync("native-generator-" + epoch, policy, CancellationToken.None)).Solution;
                    var snapshot = await SourceGeneratorCompilation.CreateAsync(published.Projects.Single(), CancellationToken.None);
                    Assert.IsNotNull(snapshot);
                    var generated = snapshot.Compilation.GetTypeByMetadataName("Owner.RegexProbe");
                    Assert.IsNotNull(generated);
                    Assert.IsTrue(generated.GetMembers("Pattern").OfType<IMethodSymbol>().Any(method => method.PartialImplementationPart is not null),
                        "The real SDK source generator must still implement the authored partial method after load and reload.");
                    Assert.IsFalse(snapshot.Compilation.GetDiagnostics().Any(diagnostic => diagnostic.Id == "CS8795"));
                    statuses.Add(loaded.Context.Availability.ToString());
                }
                catch (Exception failure) { primary = failure; throw; }
                finally { OwnedResourceCleanup.Dispose("Native preloaded analyzer cleanup failed.", primary, loaded.Workspace, loaded.Lease); }
            }
            CollectionAssert.AreEqual(Enumerable.Repeat(preload && OperatingSystem.IsWindows() ? "AnalyzerImageBindingUnavailable" : "Certified", 2).ToArray(), statuses.ToArray(),
                "Fresh native references may bind admitted images; an already-loaded foreign image never gains a certificate from current disk bytes.");
        }
        catch (Exception failure) { fixturePrimary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native generator reload fixture cleanup failed.", fixturePrimary,
                () => source is null ? ValueTask.CompletedTask : source.DisposeAsync());
        }
    }

    private sealed class EagerNativeGeneratorLoader(bool preload) : WorkspaceSessionLoader
    {
        internal IReadOnlyList<AnalyzerFileReference> PreloadedReferences { get; private set; } = [];
        protected override async Task OpenNativeAsync(MSBuildWorkspace workspace, string path, CancellationToken ct)
        {
            await base.OpenNativeAsync(workspace, path, ct);
            if (!preload) return;
            var project = workspace.CurrentSolution.Projects.Single();
            var snapshot = await SourceGeneratorCompilation.CreateAsync(project, ct);
            Assert.IsNotNull(snapshot, "The actual native snapshot must support generator execution before publication.");
            var field = typeof(AnalyzerFileReference).GetField("_lazyAssembly", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Qualify the loaded Roslyn reflection profile instead of injecting analyzer state.");
            PreloadedReferences = project.AnalyzerReferences.OfType<AnalyzerFileReference>()
                .Where(reference => field.GetValue(reference) is Assembly).ToArray();
        }
    }
}
