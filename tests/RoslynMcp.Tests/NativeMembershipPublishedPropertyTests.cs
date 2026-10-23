using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeMembershipPublishedPropertyTests
{
    [TestMethod]
    [DataRow("DefineConstants", false)]
    [DataRow("LangVersion", false)]
    [DataRow("DefineConstants", true)]
    [DataRow("LangVersion", true)]
    public async Task PublishedCertificate_RefusesAmbientCompilerOptionsDrift(string property, bool overridden)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeMembershipPublishedPropertyTests), Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(root, "Ambient.csproj");
        var ambient = "ROSLYN_PUBLISHED_" + Guid.NewGuid().ToString("N");
        var before = property == "DefineConstants" ? "BEFORE_AMBIENT" : "12.0";
        var after = property == "DefineConstants" ? "AFTER_AMBIENT" : "13.0";
        var previous = Environment.GetEnvironmentVariable(ambient);
        var policy = new OwnedTestWorkspacePathPolicy();
        var owned = new List<(IDisposable Workspace, IDisposable Lease)>();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public sealed class Entry { }");
            var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false"),
                    overridden ? new XElement(ambient, before) : null,
                    new XElement("IndirectAmbientCompilerValue", "$(" + ambient + ")"),
                    new XElement(property, "$(IndirectAmbientCompilerValue)")));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            var originalProjectBytes = await File.ReadAllBytesAsync(projectPath);
            var properties = OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>());
            Environment.SetEnvironmentVariable(ambient, before);
            var restore = await new DotnetCommandRunner().RunAsync(root, projectPath,
                MsBuildCommandProperties.Append(["restore", projectPath, "--nologo"], properties), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);

            async Task<(Solution Solution, NativeCompilerContextCertificate Context)> LoadAsync(string id)
            {
                var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync(id, projectPath, properties,
                    policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance,
                    SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                owned.Add((loaded.Workspace, loaded.Lease));
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                var projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(id, loaded.Context,
                    policy, CancellationToken.None);
                Assert.AreSame(loaded.Context, NativeCompilerContextCertificate.Require(projection.Solution));
                return (projection.Solution, loaded.Context);
            }

            var original = await LoadAsync("ambient-original");
            var originalOptions = (CSharpParseOptions)original.Solution.Projects.Single().ParseOptions!;
            AssertOptions(originalOptions, property, before);
            await original.Context.AssertCurrentAsync(original.Solution, "ambient-original", policy, CancellationToken.None);
            Environment.SetEnvironmentVariable(ambient, after);
            var fresh = await LoadAsync("ambient-fresh");
            var freshOptions = (CSharpParseOptions)fresh.Solution.Projects.Single().ParseOptions!;
            AssertOptions(freshOptions, property, overridden ? before : after);
            Assert.AreEqual(overridden, originalOptions.Equals(freshOptions),
                "The independently loaded native Roslyn parse options must prove the actual semantic premise.");
            await fresh.Context.AssertCurrentAsync(fresh.Solution, "ambient-fresh", policy, CancellationToken.None);
            if (overridden)
                await original.Context.AssertCurrentAsync(original.Solution, "ambient-original", policy, CancellationToken.None);
            else
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    original.Context.AssertCurrentAsync(original.Solution, "ambient-original", policy, CancellationToken.None));
            CollectionAssert.AreEqual(originalProjectBytes, await File.ReadAllBytesAsync(projectPath),
                "The original certificate must detect ambient drift without relying on edited project bytes.");
            Environment.SetEnvironmentVariable(ambient, before);
            await original.Context.AssertCurrentAsync(original.Solution, "ambient-original", policy, CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Published ambient context fixture cleanup failed.", primary,
                owned.AsEnumerable().Reverse().Select(pair => CleanupFailureCollector.FromAction(() =>
                    OwnedResourceCleanup.Dispose("Native ambient load cleanup failed.", primary, pair.Workspace, pair.Lease)))
                    .Prepend(CleanupFailureCollector.FromAction(() => Environment.SetEnvironmentVariable(ambient, previous)))
                    .Append(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))).ToArray());
        }
    }

    private static void AssertOptions(CSharpParseOptions options, string property, string value)
    {
        if (property == "DefineConstants")
        {
            CollectionAssert.Contains(options.PreprocessorSymbolNames.ToArray(), value);
            CollectionAssert.DoesNotContain(options.PreprocessorSymbolNames.ToArray(),
                value == "BEFORE_AMBIENT" ? "AFTER_AMBIENT" : "BEFORE_AMBIENT");
        }
        else Assert.AreEqual(value == "12.0" ? LanguageVersion.CSharp12 : LanguageVersion.CSharp13, options.LanguageVersion);
    }
}
