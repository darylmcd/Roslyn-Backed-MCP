using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeFinalCompilerUnavailableTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task FinalCompilerSelectionUncertainty_PreservesTheOwnedSemanticSnapshot(bool releaseBeforeNormalization, bool mutateAuthoredInput)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeFinalCompilerUnavailableTests), Guid.NewGuid().ToString("N"));
        CompilerSelectionMutex? selection = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            selection = new CompilerSelectionMutex();
            var dependencyRoot = Path.Combine(root, "Dependency");
            Directory.CreateDirectory(dependencyRoot);
            var dependency = Path.Combine(dependencyRoot, "Dependency.csproj");
            var project = Path.Combine(root, "Native.csproj");
            var marker = Path.Combine(root, "obj", "CustomCompiler.txt");
            await File.WriteAllTextAsync(Path.Combine(dependencyRoot, "Dependency.cs"), "public class Dependency { }");
            const string authoredSource = "public class Native { public Dependency Value => new(); }";
            const string mutation = "\n// native capture changed authored input";
            var sourcePath = Path.Combine(root, "Native.cs");
            await File.WriteAllTextAsync(sourcePath, authoredSource);
            await File.WriteAllTextAsync(dependency, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                  <ItemGroup><Compile Include="Dependency.cs" /></ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(project, """
                <Project>
                  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                    <NuGetAudit>false</NuGetAudit><UseSharedCompilation>false</UseSharedCompilation>
                  </PropertyGroup>
                  <ItemGroup><Compile Include="Native.cs" /><ProjectReference Include="Dependency/Dependency.csproj" /></ItemGroup>
                  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
                  <UsingTask TaskName="ChooseCompiler" TaskFactory="RoslynCodeTaskFactory"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                    <ParameterGroup>
                      <MutexName ParameterType="System.String" Required="true" />
                      <Architecture ParameterType="System.String" Output="true" />
                    </ParameterGroup>
                    <Task><Code Type="Fragment" Language="cs"><![CDATA[
                      using (var mutex = System.Threading.Mutex.OpenExisting(MutexName))
                      {
                          bool acquired = mutex.WaitOne(0);
                          Architecture = acquired ? "x86" : "x64";
                          if (acquired) mutex.ReleaseMutex();
                      }
                      return true;
                    ]]></Code></Task>
                  </UsingTask>
                  <UsingTask TaskName="Csc" Architecture="x64"
                             AssemblyFile="$(RoslynTargetsPath)/Microsoft.Build.Tasks.CodeAnalysis.dll" />
                  <UsingTask TaskName="Csc" Architecture="x86" Override="true" TaskFactory="RoslynCodeTaskFactory"
                             AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                    <Task>
                      <Reference Include="$(RoslynTargetsPath)/Microsoft.Build.Tasks.CodeAnalysis.dll" />
                      <Code Type="Class" Language="cs"><![CDATA[
                        public class Csc : Microsoft.CodeAnalysis.BuildTasks.Csc
                        {
                            public override bool Execute()
                            {
                                if (DefineConstants != null && DefineConstants.IndexOf("MUTATE_DURING_FINAL", System.StringComparison.Ordinal) >= 0
                                    && System.Array.Exists(System.Environment.GetCommandLineArgs(), argument => argument.StartsWith(
                                        "-logger:RoslynMcp.Roslyn.Services.CompilerReferenceCaptureLogger", System.StringComparison.Ordinal)))
                                {
                                    var source = System.Array.Find(Sources, item => System.IO.Path.GetFileName(item.ItemSpec) == "Native.cs");
                                    if (source == null) return false;
                                    System.IO.File.AppendAllText(source.ItemSpec, "\n// native capture changed authored input");
                                }
                                CommandLineArgs = new Microsoft.Build.Framework.ITaskItem[]
                                {
                                    new Microsoft.Build.Utilities.TaskItem("controlled-unverified-compiler-input")
                                };
                                return true;
                            }
                        }
                      ]]></Code>
                    </Task>
                  </UsingTask>
                  <Target Name="CoreCompile">
                    <ChooseCompiler MutexName="$(SelectionMutex)"><Output TaskParameter="Architecture" PropertyName="SelectedCompilerArchitecture" /></ChooseCompiler>
                    <Csc MSBuildArchitecture="$(SelectedCompilerArchitecture)" Sources="@(Compile)" References="@(ReferencePathWithRefAssemblies)"
                         OutputAssembly="@(IntermediateAssembly)" TargetType="Library" NoConfig="true" SkipCompilerExecution="true"
                         ProvideCommandLineArgs="true" UseSharedCompilation="false">
                      <Output TaskParameter="CommandLineArgs" ItemName="CscCommandLineArgs" />
                    </Csc>
                    <WriteLinesToFile Condition="'$(SelectedCompilerArchitecture)' == 'x86'" File="$(MSBuildProjectDirectory)/obj/CustomCompiler.txt"
                                      Lines="custom compiler completed" Overwrite="true" />
                  </Target>
                </Project>
                """);
            var properties = OwnedNativeLoadContext.Properties(project,
                new Dictionary<string, string> { ["SelectionMutex"] = selection.Name });
            if (mutateAuthoredInput) properties.Add("DefineConstants", "MUTATE_DURING_FINAL");
            // Preserve the installed SDK's complete native compiler options;
            // only the fixture's host-selection statement is changed.
            var roslynDirectory = MsBuildMetadataHelper.EvaluateProperties(dependency,
                OwnedNativeLoadContext.Properties(dependency, new Dictionary<string, string>()), "RoslynTargetsPath").Single();
            Assert.IsFalse(string.IsNullOrEmpty(roslynDirectory));
            var sdk = XDocument.Load(Path.Combine(roslynDirectory, "Microsoft.CSharp.Core.targets"));
            var sdkRoot = sdk.Root ?? throw new AssertFailedException("The installed compiler target document needs a root.");
            var compilerTarget = new XElement(sdkRoot.Elements().Single(element => element.Name.LocalName == "Target"
                && (string?)element.Attribute("Name") == "CoreCompile"));
            foreach (var element in compilerTarget.DescendantsAndSelf())
            {
                element.Name = element.Name.LocalName;
                foreach (var declaration in element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).ToArray()) declaration.Remove();
            }
            var compiler = compilerTarget.Elements().Single(element => element.Name.LocalName == "Csc");
            compiler.SetAttributeValue("MSBuildArchitecture", "$(SelectedCompilerArchitecture)");
            compiler.AddBeforeSelf(new XElement("ChooseCompiler", new XAttribute("MutexName", "$(SelectionMutex)"),
                new XElement("Output", new XAttribute("TaskParameter", "Architecture"), new XAttribute("PropertyName", "SelectedCompilerArchitecture"))));
            compiler.AddAfterSelf(new XElement("WriteLinesToFile", new XAttribute("Condition", "'$(SelectedCompilerArchitecture)' == 'x86'"),
                new XAttribute("File", marker), new XAttribute("Lines", "custom compiler completed"), new XAttribute("Overwrite", "true")));
            var definition = XDocument.Load(project);
            var fixtureRoot = definition.Root ?? throw new AssertFailedException("The controlled native fixture needs a root.");
            fixtureRoot.Elements("Target").Single(element => (string?)element.Attribute("Name") == "CoreCompile").ReplaceWith(compilerTarget);
            await File.WriteAllTextAsync(project, definition.ToString());
            var restore = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--disable-parallel"], properties), CancellationToken.None);
            var restoreCodes = Regex.Matches(restore.StdOut + restore.StdErr, @"\b(?:MSB|CS|NU|NETSDK)\d{4}\b")
                .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var safeAttributes = new HashSet<string>(StringComparer.Ordinal)
                { "xmlns", "Label", "Returns", "KeepDuplicateOutputs", "BeforeTargets", "AfterTargets", "Condition", "Name", "DependsOnTargets", "Inputs", "Outputs", "MSBuildArchitecture", "TaskFactory" };
            var attributes = Regex.Matches(restore.StdOut + restore.StdErr, "\\battribute [\\\"'](?<name>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase)
                .Select(match => safeAttributes.Contains(match.Groups["name"].Value) ? match.Groups["name"].Value : "other")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Assert.IsTrue(restore.Succeeded, "The native project-edge control must restore before owned loading; codes=" + string.Join(",", restoreCodes)
                + ";attributes=" + string.Join(",", attributes));
            var policy = new OwnedTestWorkspacePathPolicy();
            async Task ChangeSelectionAsync(MSBuildWorkspace native, CancellationToken ct)
            {
                if (!releaseBeforeNormalization) return;
                Assert.IsTrue(native.CurrentSolution.Projects.Any(owner => owner.Documents.Any(document => document.Name == "Native.cs")));
                Assert.IsFalse(File.Exists(marker), "Native preparation must select the builtin before the controlled runtime selection changes.");
                selection.Release();
                var independent = await new DotnetCommandRunner().RunAsync(root, project,
                    MsBuildCommandProperties.Append(["msbuild", project, "-nologo", "-m:1", "-nr:false", "-target:Compile;CoreCompile"],
                        NativeCompilerTaskInputs.EffectiveGlobals(properties, "net10.0")), CancellationToken.None);
                var codes = Regex.Matches(independent.StdOut + independent.StdErr, @"\b(?:MSB|CS|NU|NETSDK)\d{4}\b")
                    .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var output = independent.StdOut + independent.StdErr;
                var exceptionTypes = new[] { "InvalidOperationException", "NullReferenceException", "FileNotFoundException", "FileLoadException",
                    "TypeLoadException", "MissingMethodException", "InvalidCastException", "NotSupportedException", "PlatformNotSupportedException",
                    "TargetInvocationException", "ArgumentException" }.Where(name => output.Contains(name, StringComparison.Ordinal));
                var reasons = new Dictionary<string, string>
                {
                    ["Could not load file or assembly"] = "assembly-load",
                    ["Object reference not set"] = "null-reference",
                    ["Unable to cast"] = "invalid-cast",
                    ["is not supported"] = "unsupported",
                    ["already been initialized"] = "initialization",
                    ["Could not find"] = "missing",
                    ["ToolName"] = "tool-name"
                }.Where(pair => output.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value);
                Assert.IsTrue(independent.Succeeded, "The released native compiler control must complete; codes=" + string.Join(",", codes)
                    + ";exceptions=" + string.Join(",", exceptionTypes) + ";reasons=" + string.Join(",", reasons));
                Assert.AreEqual("custom compiler completed", (await File.ReadAllTextAsync(marker)).Trim());
                File.Delete(marker);
            }
            var loader = new SelectionChangingLoader(ChangeSelectionAsync);
            async Task LoadAsync()
            {
                var loaded = await loader.CreateAndOpenAsync("final-compiler-selection", project, properties,
                    policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                Exception? loadFailure = null;
                try
                {
                    var projection = await loaded.Context.NormalizeAsync("final-compiler-selection", policy, CancellationToken.None);
                    Assert.IsTrue(projection.Solution.Projects.Any(owner => owner.Documents.Any(document => document.Name == "Native.cs")));
                    if (releaseBeforeNormalization)
                    {
                        Assert.AreEqual(NativeCompilerContextAvailability.TaskExecutionUnavailable, loaded.Context.Availability);
                        var refusal = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(projection.Solution));
                        StringAssert.Contains(refusal.Message, "task execution");
                        _ = Assert.ThrowsExactly<PublicInvalidOperationException>(() => { _ = loaded.Context.InstalledPackages; });
                        var imageOwner = loaded.Context.NativeSolution.Projects.First(owner => owner.MetadataReferences.OfType<PortableExecutableReference>().Any());
                        var image = imageOwner.MetadataReferences.OfType<PortableExecutableReference>().First();
                        var imageRefusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => loaded.Context.ReadAdmittedImagesAsync(
                            loaded.Context.NativeSolution, imageOwner.Id, image, "final-compiler-selection", policy, CancellationToken.None));
                        StringAssert.Contains(imageRefusal.Message, "task execution");
                    }
                    else
                    {
                        Assert.IsFalse(File.Exists(marker), "The unchanged native selection must never execute the custom compiler.");
                        Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(projection.Solution).Availability);
                    }
                }
                catch (Exception failure) { loadFailure = failure; throw; }
                finally { OwnedResourceCleanup.Dispose("Final compiler load cleanup failed.", loadFailure, loaded.Workspace, loaded.Lease); }
            }
            Exception? observedFailure = null;
            try { await LoadAsync(); }
            catch (Exception failure) { observedFailure = failure; }
            if (releaseBeforeNormalization)
                Assert.AreEqual("custom compiler completed", (await File.ReadAllTextAsync(marker)).Trim(),
                    "The actual owned final child must finish the selected custom compiler before issuing uncertainty.");
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(authoredSource + (mutateAuthoredInput ? mutation : "")),
                await File.ReadAllBytesAsync(sourcePath), "Only the controlled captured custom compiler may change the exact authored bytes.");
            if (mutateAuthoredInput)
            {
                var drift = observedFailure as PublicInvalidOperationException;
                Assert.IsNotNull(drift, "Authored drift must refuse the load before publishing an unavailable semantic projection.");
                StringAssert.Contains(drift.Message, "Physical execution inputs changed after preparation");
                Assert.IsNotNull(loader.NativeSnapshot);
                _ = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(loader.NativeSnapshot));
                return;
            }
            if (observedFailure is not null) ExceptionDispatchInfo.Capture(observedFailure).Throw();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Final compiler fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => selection?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class SelectionChangingLoader(Func<MSBuildWorkspace, CancellationToken, Task> afterOpen) : WorkspaceSessionLoader
    {
        private bool _selectionObserved;
        internal Solution? NativeSnapshot { get; private set; }
        protected override async Task OpenNativeAsync(MSBuildWorkspace workspace, string path, CancellationToken ct)
        {
            await base.OpenNativeAsync(workspace, path, ct);
            NativeSnapshot = workspace.CurrentSolution;
            if (_selectionObserved) return;
            _selectionObserved = true;
            await afterOpen(workspace, ct);
        }
    }

    private sealed class CompilerSelectionMutex : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Mutex _mutex;
        private readonly Thread _owner;
        private Exception? _failure;
        private bool _disposed;
        internal string Name { get; } = "RoslynFinalCompiler" + Guid.NewGuid().ToString("N");
        internal CompilerSelectionMutex()
        {
            // Keep the named object alive after its owning thread unlocks it,
            // so the subsequent native child can still OpenExisting.
            _mutex = new Mutex(false, Name);
            Thread? startedOwner = null;
            try
            {
                _owner = new Thread(() =>
                {
                    try
                    {
                        _mutex.WaitOne();
                        try { _ready.Set(); _release.Wait(); }
                        finally { _mutex.ReleaseMutex(); }
                    }
                    catch (Exception failure) { _failure = failure; _ready.Set(); }
                });
                _owner.Start();
                startedOwner = _owner;
                _ready.Wait();
                if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Compiler selection mutex acquisition failed.", failure,
                    OwnedResourceCleanup.FromAction(() => { if (startedOwner is { } owner) { _release.Set(); owner.Join(); } }),
                    _mutex, _ready, _release);
                throw;
            }
        }
        internal void Release()
        {
            _release.Set();
            _owner.Join();
            if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? primary = null;
            try { Release(); }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Compiler selection mutex release failed.", primary,
                    _mutex, _ready, _release);
            }
        }
    }
}
