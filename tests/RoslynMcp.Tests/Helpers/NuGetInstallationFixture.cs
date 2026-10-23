using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Creates a local feed, then delegates installation and its manifests to real NuGet restore.</summary>
internal sealed class NuGetInstallationFixture : IDisposable
{
    internal string Root { get; }
    internal string Cache => Path.Combine(Root, "cache");
    internal string Assets => Path.Combine(Root, "obj", "project.assets.json");
    internal Dictionary<string, string> LoadProperties => OwnedNativeLoadContext.Properties(Path.Combine(Root, "Owner.csproj"),
        new Dictionary<string, string>());
    private NuGetInstallationFixture(string root) => Root = root;

    internal static async Task<NuGetInstallationFixture> CreateAsync(string id, string version, IReadOnlyDictionary<string, byte[]> files)
    {
        var fixture = new NuGetInstallationFixture(Path.Combine(TestTempRoot.Current, "installed-package-" + Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(fixture.Root);
            var feed = Path.Combine(fixture.Root, "feed");
            Directory.CreateDirectory(feed);
            var packagePath = Path.Combine(feed, id + "." + version + ".nupkg");
            var nuspec = new XElement("package", new XElement("metadata", new XElement("id", id),
                new XElement("version", version), new XElement("authors", "Fixture"), new XElement("description", "Owned local restore fixture")));
            var entries = new Dictionary<string, byte[]>(files) { [id + ".nuspec"] = Encoding.UTF8.GetBytes(nuspec.ToString()) };
            await WriteArchiveAsync(packagePath, entries);
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", id), new XAttribute("Version", version))));
            await File.WriteAllTextAsync(project, xml.ToString());
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Directory.Build.props"), "<Project />");
            using var restoreDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var execution = await new DotnetCommandRunner().RunAsync(fixture.Root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo", "--source", feed, "--packages", fixture.Cache], fixture.LoadProperties), restoreDeadline.Token);
            var diagnostics = System.Text.RegularExpressions.Regex.Matches(execution.StdOut + execution.StdErr, @"\bNU\d{4}\b")
                .Select(match => match.Value).Distinct(StringComparer.Ordinal);
            Assert.IsTrue(execution.Succeeded, "The owned local NuGet installation must succeed before testing its authority. Exit="
                + execution.ExitCode + "; NuGet diagnostics=" + string.Join(",", diagnostics));
            return fixture;
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Local package fixture cleanup failed.", failure, fixture);
            throw;
        }
    }

    internal static async Task WriteArchiveAsync(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        var stream = new FileStream(path, FileMode.CreateNew);
        Exception? streamFailure = null;
        try
        {
            var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            Exception? archiveFailure = null;
            try
            {
                foreach (var file in files)
                {
                    var output = archive.CreateEntry(file.Key).Open();
                    Exception? outputFailure = null;
                    try
                    {
                        await output.WriteAsync(file.Value);
                    }
                    catch (Exception failure) { outputFailure = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Local package entry cleanup failed.", outputFailure,
                            output.DisposeAsync);
                    }
                }
            }
            catch (Exception failure) { archiveFailure = failure; throw; }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Local package archive cleanup failed.", archiveFailure, archive);
            }
        }
        catch (Exception failure) { streamFailure = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Local package stream cleanup failed.", streamFailure,
                stream.DisposeAsync);
        }
    }
    public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
}
