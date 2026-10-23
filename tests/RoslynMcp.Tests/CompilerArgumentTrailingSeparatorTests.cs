using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentTrailingSeparatorTests
{
    [TestMethod]
    [DataRow("file")]
    [DataRow("directory")]
    [DataRow("missing")]
    public async Task RecursiveDirectorySuffix_PreservesNativeMembershipAndDiagnostics(string kind)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentTrailingSeparatorTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var entry = Path.Combine(root, "entry");
            if (kind == "file") await File.WriteAllTextAsync(entry, "an ordinary file");
            if (kind == "directory")
            {
                Directory.CreateDirectory(entry);
                await File.WriteAllTextAsync(Path.Combine(entry, "Member.cs"), "class Member { }");
            }
            ImmutableArray<string> arguments = ["/recurse:\"" + entry + Path.DirectorySeparatorChar + "*.cs\""];
            var native = CSharpCommandLineParser.Default.Parse(arguments, root, sdkDirectory: null);
            var policy = new OwnedTestWorkspacePathPolicy();
            var parser = new CompilerArgumentInputContext(root,
                (paths, token) => policy.ValidateAsync("suffix-native-oracle", paths, token),
                (path, token) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadAllBytesAsync, token),
                (path, token) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, token),
                CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var observed = await parser.ParseAsync(arguments, CancellationToken.None);
            CollectionAssert.AreEqual(native.SourceFiles.Select(file => file.Path).ToArray(),
                observed.Arguments.SourceFiles.Select(file => file.Path).ToArray());
            CollectionAssert.AreEqual(native.Errors.Select(error => (error.Id, error.Severity)).ToArray(),
                observed.Arguments.Errors.Select(error => (error.Id, error.Severity)).ToArray(),
                "The held input boundary must preserve actual native diagnostics for file, directory and missing operands.");
            await observed.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Compiler trailing separator fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))); }
    }
}
