using RoslynMcp.Core.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>Creates process and filesystem fixtures owned by one native admission test case.</summary>
public abstract class ForkNativeAdmissionTestBase : IsolatedWorkspaceTestBase
{
    protected sealed class NativeFixture : IDisposable
    {
        internal string Root { get; }
        internal string Source => Path.Combine(Root, "source");
        internal string Fork => Path.Combine(Source, "fork");
        internal string Evidence => Path.Combine(Root, "evidence");
        internal string ProtectedOutput => Path.Combine(Source, "protected.txt");

        internal NativeFixture(bool quoted)
            : this(Path.Combine(TestTempRoot.Current, "fork-native-" + (quoted ? "quoted'-" : "") + Guid.NewGuid().ToString("N")))
        {
        }

        // Keep both legacy nesting levels native-launchable beneath the hosted test root.
        internal static NativeFixture CreateNestedLegacy() => new(Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N")));

        private NativeFixture(string root)
        {
            Root = root;
            Directory.CreateDirectory(Fork);
            Directory.CreateDirectory(Evidence);
        }

        internal string PathInFork(string name) => Path.Combine(Fork, name);

        internal void WriteProject(string path, string body)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems>
                    <OutDir>bin/</OutDir><NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                """ + body + "</Project>");
        }

        internal async Task RestoreAsync(string path, IDotnetCommandRunner runner, CancellationToken ct)
        {
            var restore = await runner.RunAsync(Fork, path, ["restore", path, "--nologo", "-nr:false"], ct);
            Assert.IsTrue(restore.Succeeded, restore.Describe());
        }

        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }
}
