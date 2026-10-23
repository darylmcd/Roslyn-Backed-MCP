using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeUsingTaskConditionTests
{
    [TestMethod]
    [DataRow(false, "bytes", false)]
    [DataRow(true, "bytes", false)]
    [DataRow(false, "clone", false)]
    [DataRow(true, "clone", false)]
    [DataRow(false, "clone", true)]
    [DataRow(true, "clone", true)]
    public async Task PrivateCollectionCache_DoesNotMutateIndependentRootOrSdkImports(bool sdk, string representation, bool imported)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        await UsingTaskFixture.RunAsync(async fixture =>
        {
            var (sourcePath, changedPath) = VerifyIndependentCollections(fixture, sdk, representation, imported);
            var bytes = File.ReadAllBytes(sourcePath);
            Assert.IsTrue(await fixture.BuildAsync(sourcePath, fixture.SourceReceipt));
            Assert.IsFalse(await fixture.BuildAsync(changedPath, fixture.PrivateReceipt));
            Assert.AreEqual("registered", File.ReadAllText(fixture.SourceReceipt));
            Assert.IsFalse(File.Exists(fixture.PrivateReceipt));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(sourcePath));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string SourcePath, string ChangedPath) VerifyIndependentCollections(UsingTaskFixture fixture, bool sdk, string representation, bool imported)
    {
        var sourcePath = fixture.WriteDefinition("'true' == 'true'", imported, sdk, copies: 1);
        var bytes = File.ReadAllBytes(sourcePath);
        Microsoft.Build.Evaluation.ProjectCollection? first = null;
        Microsoft.Build.Evaluation.ProjectCollection? independent = null;
        Exception? primary = null;
        try
        {
            first = new Microsoft.Build.Evaluation.ProjectCollection();
            independent = new Microsoft.Build.Evaluation.ProjectCollection();
            var other = independent.LoadProject(sourcePath, fixture.Globals(fixture.SourceReceipt), toolsVersion: null);
            var otherRoots = other.Imports.Select(import => import.ImportedProject).Prepend(other.Xml)
                .DistinctBy(root => root.FullPath, FileSystemPath.Comparer).ToDictionary(root => root.FullPath, FileSystemPath.Comparer);
            var originalConditions = otherRoots.ToDictionary(pair => pair.Key,
                pair => pair.Value.AllChildren.Select(element => (element.ElementName, element.Condition)).ToArray(), FileSystemPath.Comparer);
            var originalBytes = otherRoots.ToDictionary(pair => pair.Key, pair => File.ReadAllBytes(pair.Key), FileSystemPath.Comparer);
            var added = new List<Microsoft.Build.Construction.ProjectRootElement>();
            first.ProjectAdded += (_, message) =>
            {
                added.Add(message.ProjectRootElement);
                // Native SDK imports can use readonly XML documents. Rehydrate only
                // this private cache entry through the public reader overload.
                using var stream = new MemoryStream(originalBytes[message.ProjectRootElement.FullPath], writable: false);
                using var serialized = representation switch
                {
                    "bytes" => null,
                    "clone" => new StringReader(message.ProjectRootElement.DeepClone().RawXml),
                    _ => throw new ArgumentOutOfRangeException(nameof(representation))
                };
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
                using var reader = serialized is null ? XmlReader.Create(stream, settings) : XmlReader.Create(serialized, settings);
                message.ProjectRootElement.ReloadFrom(reader, throwIfUnsavedChanges: false, preserveFormatting: true);
                foreach (var task in message.ProjectRootElement.UsingTasks) task.Condition = "'false' == 'true'";
            };
            var changed = first.LoadProject(sourcePath, fixture.Globals(fixture.PrivateReceipt), toolsVersion: null);
            Assert.IsTrue(added.Count > 0, "The public cache event must run before the private native evaluation.");
            var changedRoots = changed.Imports.Select(import => import.ImportedProject).Prepend(changed.Xml)
                .DistinctBy(root => root.FullPath, FileSystemPath.Comparer).ToDictionary(root => root.FullPath, FileSystemPath.Comparer);
            if (sdk) Assert.IsTrue(otherRoots.Values.Any(root => root != other.Xml && root.UsingTasks.Count > 0));
            foreach (var (path, root) in otherRoots)
            {
                CollectionAssert.AreEqual(originalConditions[path], root.AllChildren.Select(element => (element.ElementName, element.Condition)).ToArray(),
                    "Another collection's native condition inventory must remain exact.");
                CollectionAssert.AreEqual(SHA256.HashData(originalBytes[path]), SHA256.HashData(File.ReadAllBytes(path)), "Private XML mutation must never persist.");
                if (changedRoots.TryGetValue(path, out var changedRoot)) Assert.AreNotSame(root, changedRoot);
            }
            if (imported)
            {
                var taskImport = Path.Combine(fixture.Source, "imports", "task.props");
                File.WriteAllText(Path.Combine(fixture.Private, "imports", "task.props"), changedRoots[taskImport].RawXml);
            }
            var changedPath = Path.Combine(imported ? fixture.Private : fixture.Source, "changed.proj");
            File.WriteAllText(changedPath, changed.Xml.RawXml);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(sourcePath));
            return (sourcePath, changedPath);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Independent native collections cleanup failed.", primary, independent, first); }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task UsingTaskPredicate_PreservesNativeRegistrationAndPrivateMarker(bool imported, bool privateMarker)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        await UsingTaskFixture.RunAsync(fixture => fixture.ReplayAsync(imported, privateMarker));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task UsingTaskPredicate_EvaluatesOriginalExistsExactlyOncePerNativeEvaluation(bool duplicate)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        return UsingTaskFixture.RunAsync(fixture =>
        {
            fixture.VerifyCount(duplicate);
            return Task.CompletedTask;
        });
    }

    private sealed class UsingTaskFixture
    {
        private readonly string _root = Path.Combine(TestTempRoot.Current, "native-usingtask", Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(_root, "source");
        internal string Private => Path.Combine(_root, "private");
        internal string SourceReceipt => Path.Combine(_root, "source.receipt");
        internal string PrivateReceipt => Path.Combine(_root, "private.receipt");
        private string TaskAssembly => Path.Combine(_root, "NativeConditionProbe.dll");

        internal static async Task RunAsync(Func<UsingTaskFixture, Task> body)
        {
            var fixture = new UsingTaskFixture();
            Exception? primary = null;
            try
            {
                Directory.CreateDirectory(fixture.Source);
                Directory.CreateDirectory(fixture.Private);
                fixture.CompileTask();
                await body(fixture);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Native UsingTask fixture cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture._root)));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CompileTask()
        {
            const string code = """
                public sealed class NativeConditionProbe : Microsoft.Build.Framework.ITask
                {
                    public Microsoft.Build.Framework.IBuildEngine BuildEngine { get; set; }
                    public Microsoft.Build.Framework.ITaskHost HostObject { get; set; }
                    public string Receipt { get; set; }
                    public bool Execute() { System.IO.File.AppendAllText(Receipt, "registered"); return true; }
                }
                """;
            var platform = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new AssertFailedException("The native task fixture requires the current runtime reference inventory.");
            var references = platform.Split(Path.PathSeparator).Append(typeof(Microsoft.Build.Framework.ITask).Assembly.Location)
                .Distinct(FileSystemPath.Comparer).Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("NativeConditionProbe", [CSharpSyntaxTree.ParseText(code)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = compilation.Emit(TaskAssembly);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        internal Dictionary<string, string> Globals(string receipt) => new() { ["Root"] = Source, ["Receipt"] = receipt };

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal async Task<bool> BuildAsync(string projectPath, string receipt)
        {
            var sdkDirectory = Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)
                ?? throw new AssertFailedException("The selected native MSBuild assembly must have an SDK directory.");
            var msbuild = Path.Combine(sdkDirectory, "MSBuild.dll");
            var arguments = new List<string> { "exec", msbuild, projectPath, "/t:Probe", "/nr:false", "/nologo", "/v:quiet" };
            foreach (var (name, value) in Globals(receipt))
                arguments.Add("/p:" + name + "=" + Microsoft.Build.Evaluation.ProjectCollection.Escape(value));
            // The child owns every loaded fixture task assembly. Its exit ends
            // that lifetime before fixture/assembly cleanup removes the DLL.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var result = await new DotnetCommandRunner().RunAsync(Source, projectPath, arguments,
                earlyKillPatterns: null, executablePath: Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", timeout.Token);
            return result.Succeeded;
        }

        internal string WriteDefinition(string condition, bool imported, bool sdk, int copies)
        {
            var usingTask = new XElement("UsingTask", new XAttribute("TaskName", "NativeConditionProbe"),
                new XAttribute("AssemblyFile", TaskAssembly), new XAttribute("Condition", condition));
            var project = new XElement("Project");
            if (sdk)
            {
                project.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
                project.Add(new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0")));
            }
            if (imported)
            {
                Directory.CreateDirectory(Path.Combine(Source, "imports"));
                Directory.CreateDirectory(Path.Combine(Private, "imports"));
                File.WriteAllText(Path.Combine(Source, "imports", "task.props"), new XElement("Project",
                    Enumerable.Range(0, copies).Select(_ => new XElement(usingTask))).ToString());
                project.Add(new XElement("Import", new XAttribute("Project", "imports/task.props")));
            }
            else project.Add(Enumerable.Range(0, copies).Select(_ => new XElement(usingTask)));
            project.Add(new XElement("Target", new XAttribute("Name", "Probe"),
                new XElement("NativeConditionProbe", new XAttribute("Receipt", "$(Receipt)"))));
            var path = Path.Combine(Source, "task.proj");
            File.WriteAllText(path, project.ToString());
            return path;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal async Task ReplayAsync(bool imported, bool privateMarker)
        {
            const string condition = "Exists('$(Root)/marker.flag') And ('true' == 'true' Or Exists('$(Root)/unqueried.flag'))";
            var path = WriteDefinition(condition, imported, sdk: false, copies: 1);
            File.WriteAllText(Path.Combine(Source, "marker.flag"), "source");
            if (privateMarker) File.WriteAllText(Path.Combine(Private, "marker.flag"), "private");
            Assert.IsTrue(await BuildAsync(path, SourceReceipt),
                "The independent native source must register and execute the task before replay.");
            var sourceBytes = File.ReadAllBytes(path);
            var graph = EvaluatedBuildGraph.FromRoots([path], Globals(SourceReceipt), ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var evidence = graph.Nodes.Single().PathEvidence;
            string Map(string value) => FileSystemPath.Comparer.Equals(Source, value) || FileSystemPath.IsStrictDescendant(Source, value)
                ? Path.Combine(Private, Path.GetRelativePath(Source, value)) : value;
            var privatePath = Path.Combine(Private, "task.proj");
            File.WriteAllBytes(privatePath, SolutionFileMutationPlanner.RelocateDefinition(path, sourceBytes, Map, Map,
                BuildPathRelocation.KnownPathProperties, evidence.Declarations));
            if (imported)
            {
                var import = Path.Combine(Source, "imports", "task.props");
                File.WriteAllBytes(Path.Combine(Private, "imports", "task.props"),
                    SolutionFileMutationPlanner.RelocateDefinition(import, File.ReadAllBytes(import), Map, Map,
                        BuildPathRelocation.KnownPathProperties, evidence.Declarations));
            }
            Assert.AreEqual(privateMarker, await BuildAsync(privatePath, PrivateReceipt));
            Assert.AreEqual(privateMarker, File.Exists(PrivateReceipt));
            if (privateMarker) Assert.AreEqual("registered", File.ReadAllText(PrivateReceipt));
            Assert.AreEqual("registered", File.ReadAllText(SourceReceipt));
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(path));
            Assert.IsFalse(evidence.ConditionProbes.Any(probe => probe.Path.EndsWith("unqueried.flag", StringComparison.Ordinal)));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void VerifyCount(bool duplicate)
        {
            var marker = Path.Combine(Source, "marker.flag");
            var skipped = Path.Combine(Source, "unqueried.flag");
            File.WriteAllText(marker, "source");
            const string condition = "Exists('$(Root)/marker.flag') And ('true' == 'true' Or Exists('$(Root)/unqueried.flag'))";
            var copies = duplicate ? 2 : 1;
            var path = WriteDefinition(condition, imported: false, sdk: false, copies);
            NativeConditionEvidence? observer = null;
            Exception? primary = null;
            try
            {
                observer = new NativeConditionEvidence();
                var owner = observer.Collection;
                owner.RegisterLogger(observer);
                observer.VerifyProtocol(owner);
                for (var index = 0; index < 2; index++)
                {
                    observer.Begin();
                    var project = Microsoft.Build.Evaluation.Project.FromProjectRootElement(
                        Microsoft.Build.Construction.ProjectRootElement.Open(path, owner, preserveFormatting: true),
                        observer.Options(Globals(SourceReceipt), owner));
                    Assert.AreEqual(copies, observer.CountExistsProbes(marker), "Each native task condition must evaluate its original Exists operand exactly once.");
                    Assert.AreEqual(0, observer.CountExistsProbes(skipped), "Native short-circuit evaluation must not query the skipped operand.");
                    observer.Bind(project);
                    Assert.AreEqual(copies, project.Xml.UsingTasks.Sum(task => observer.Executions(task).Count));
                    owner.UnloadProject(project);
                }
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native UsingTask observer cleanup failed.", primary, observer); }
        }

    }
}
