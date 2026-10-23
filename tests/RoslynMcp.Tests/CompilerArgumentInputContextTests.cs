using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentInputContextTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualNestedResponseFiles_AuthorizeBeforeOwnParserReads(bool denyNested)
    {
        var fixture = await CreateAsync();
        MSBuildWorkspace? native = null;
        Exception? primary = null;
        try
        {
            native = MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(fixture.Project, new Dictionary<string, string>()));
            var project = await native.OpenProjectAsync(fixture.Project);
            var options = project.ParseOptions as CSharpParseOptions ?? throw new AssertFailedException("Native fixture did not load C# options.");
            CollectionAssert.Contains(options.PreprocessorSymbolNames.ToArray(), "OUTER_RESPONSE");
            CollectionAssert.Contains(options.PreprocessorSymbolNames.ToArray(), "NESTED_RESPONSE");
            var denied = denyNested ? fixture.Nested : fixture.Outer;
            var unauthorizedReads = 0;
            var authorized = new HashSet<string>(FileSystemPath.Comparer);
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                foreach (var path in paths)
                {
                    if (FileSystemPath.Comparer.Equals(path, denied)) throw new UnauthorizedAccessException("Synthetic compiler input denial.");
                    authorized.Add(path);
                }
                return Task.CompletedTask;
            }
            async Task<byte[]?> Read(string path, CancellationToken ct)
            {
                if (!authorized.Contains(path)) unauthorizedReads++;
                return await File.ReadAllBytesAsync(path, ct);
            }
            var context = new CompilerArgumentInputContext(fixture.Root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => context.ParseAsync(["@\"outer response.rsp\""], CancellationToken.None));
            Assert.AreEqual(0, unauthorizedReads);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler response fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => native?.Dispose()),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Root)));
        }
    }

    [TestMethod]
    public async Task ActualNestedResponseFiles_FreezeEveryParsedByteAndDetectLaterDrift()
    {
        var fixture = await CreateAsync();
        Exception? primary = null;
        try
        {
            var approved = new HashSet<string>(FileSystemPath.Comparer);
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) { approved.UnionWith(paths); return Task.CompletedTask; }
            async Task<byte[]?> Read(string path, CancellationToken ct)
            {
                Assert.IsTrue(approved.Contains(path), "Compiler inputs must be authorized before each explicit read.");
                return await File.ReadAllBytesAsync(path, ct);
            }
            var context = new CompilerArgumentInputContext(fixture.Root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var receipt = await context.ParseAsync(["@\"outer response.rsp\""], CancellationToken.None);
            CollectionAssert.Contains(receipt.Arguments.ParseOptions.PreprocessorSymbolNames.ToArray(), "OUTER_RESPONSE");
            CollectionAssert.Contains(receipt.Arguments.ParseOptions.PreprocessorSymbolNames.ToArray(), "NESTED_RESPONSE");
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            await File.WriteAllTextAsync(fixture.Nested, "/define:CHANGED_RESPONSE");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
            CollectionAssert.AreEquivalent(new[] { fixture.Outer, fixture.Nested }, receipt.Inputs.Select(input => input.FilePath).ToArray());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler response fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Root)));
        }
    }

    private static async Task<(string Root, string Project, string Outer, string Nested)> CreateAsync()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentInputContextTests), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            var outer = Path.Combine(root, "outer response.rsp");
            var nested = Path.Combine(root, "nested.rsp");
            await File.WriteAllTextAsync(outer, "/define:OUTER_RESPONSE\n@nested.rsp\n");
            await File.WriteAllTextAsync(nested, "/define:NESTED_RESPONSE\n");
            var project = Path.Combine(root, "Response.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><CompilerResponseFile>outer response.rsp</CompilerResponseFile></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root, "Response.cs"), "public class Response { }");
            var restore = await new DotnetCommandRunner().RunAsync(root, project, ["restore", project, "--nologo"], CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
            return (root, project, outer, nested);
        }
        catch (Exception failure)
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler response fixture preparation failed.", failure,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
            throw;
        }
    }
}
