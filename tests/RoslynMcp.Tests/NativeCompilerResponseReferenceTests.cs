using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerResponseReferenceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResponseReference_SearchAndInteropMetadataRemainActualCompilerInputs(bool linked)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerResponseReferenceTests), Guid.NewGuid().ToString("N"));
        MSBuildWorkspace? native = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            var references = Path.Combine(root, "references");
            Directory.CreateDirectory(references);
            var contract = Path.Combine(references, "Contract.dll");
            var compilation = CSharpCompilation.Create("Contract", [CSharpSyntaxTree.ParseText("""
                using System.Runtime.InteropServices;
                [assembly: Guid("527E4788-D94D-40CC-88CA-CA4F740E32A1")]
                [assembly: ImportedFromTypeLib("Contract")]
                [ComImport, Guid("5783904E-959A-4098-A539-765A8F1D3645"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IContract { void Invoke(); }
                """)], new[] { typeof(object).Assembly.Location, typeof(System.Runtime.InteropServices.ImportedFromTypeLibAttribute).Assembly.Location,
                    System.Reflection.Assembly.Load("System.Runtime").Location }.Distinct(FileSystemPath.Comparer).Select(path => MetadataReference.CreateFromFile(path)),
                new(OutputKind.DynamicallyLinkedLibrary));
            var emitted = compilation.Emit(contract);
            Assert.IsTrue(emitted.Success, string.Join(";", emitted.Diagnostics));
            var owner = Path.Combine(root, "App");
            Directory.CreateDirectory(owner);
            var projectPath = Path.Combine(owner, "App.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><CompilerResponseFile>args.rsp</CompilerResponseFile></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(owner, "Entry.cs"), "public class Entry { }");
            await File.WriteAllTextAsync(Path.Combine(owner, "args.rsp"), "/lib:../references\n" + (linked ? "/link:" : "/reference:ContractAlias=") + "Contract.dll\n");
            var restored = await new DotnetCommandRunner().RunAsync(owner, projectPath, ["restore", projectPath, "--nologo"], CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, restored.StdOut + restored.StdErr);
            native = MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()));
            var project = await native.OpenProjectAsync(projectPath);
            var nativeReference = project.MetadataReferences.OfType<PortableExecutableReference>().Single(reference => FileSystemPath.Comparer.Equals(reference.FilePath, contract));
            Assert.AreEqual(linked, nativeReference.Properties.EmbedInteropTypes);
            if (!linked) CollectionAssert.Contains(nativeReference.Properties.Aliases.ToArray(), "ContractAlias");
            var parsing = OwnedNativeCompilerInputContext.Create(project);
            var toolsVersion = OwnedNativeCompilerInputContext.CaptureToolsVersion(projectPath, native.Properties);
            var observed = await NativeCompilerTaskInputs.ReadAsync(project, native.Properties, "net10.0", toolsVersion, ["Compile", "CoreCompile"], parsing, CancellationToken.None);
            var actual = observed.References.Single(reference => FileSystemPath.Comparer.Equals(reference.Path, contract));
            Assert.AreEqual(linked, actual.EmbedInteropTypes);
            if (!linked) CollectionAssert.Contains(actual.Aliases.ToArray(), "ContractAlias");
            await parsing.AssertCurrentAsync(observed.Parsing, CancellationToken.None);
            await File.WriteAllBytesAsync(contract, [1, 2, 3]);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => parsing.AssertCurrentAsync(observed.Parsing, CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Response reference fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => native?.Dispose()),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
