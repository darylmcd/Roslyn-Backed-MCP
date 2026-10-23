using RoslynMcp.Roslyn.Helpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Copies only independently validated archives from the running test project's selected test-package closure.</summary>
internal static class ResolvedTestPackageFeed
{
    internal static async Task<IReadOnlyDictionary<string, string>> PopulateAsync(string ownedRoot, string feed, CancellationToken ct)
    {
        var repository = TestFixtureFileSystem.FindRepositoryRoot();
        var source = Path.Combine(repository, "tests", "RoslynMcp.Tests", "obj", "project.assets.json");
        var policy = new OwnedTestWorkspacePathPolicy();
        await policy.ValidateAsync("test-feed", [source, ownedRoot, feed], ct);
        var sourceIdentity = await PhysicalInputIdentity.CaptureAsync(source, ct);
        JsonDocument? actual = null;
        Exception? primary = null;
        try
        {
            actual = JsonDocument.Parse(await File.ReadAllBytesAsync(source, ct));
            var versions = XDocument.Load(Path.Combine(repository, "Directory.Packages.props")).Descendants("PackageVersion")
                .Where(element => new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" }.Contains((string?)element.Attribute("Include"), StringComparer.OrdinalIgnoreCase))
                .ToDictionary(element => (string)element.Attribute("Include")!, element => (string)element.Attribute("Version")!, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(3, versions.Count);
            var target = actual.RootElement.GetProperty("targets").EnumerateObject().Single(value => !value.Name.Contains('/')).Value;
            var packages = target.EnumerateObject().Where(value => value.Value.GetProperty("type").GetString() == "package")
                .ToDictionary(value => value.Name[..value.Name.LastIndexOf('/')], value => value, StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(versions.Keys);
            foreach (var version in versions)
                Assert.AreEqual(version.Value, packages[version.Key].Name[(packages[version.Key].Name.LastIndexOf('/') + 1)..],
                    "The running test assets must select the current configured package version.");
            while (pending.TryDequeue(out var id))
            {
                ct.ThrowIfCancellationRequested();
                var package = packages[id];
                if (!selected.Add(package.Name)) continue;
                if (package.Value.TryGetProperty("dependencies", out var dependencies))
                    foreach (var dependency in dependencies.EnumerateObject()) pending.Enqueue(dependency.Name);
            }
            var libraries = new JsonObject();
            foreach (var identity in selected)
                libraries.Add(identity, JsonNode.Parse(actual.RootElement.GetProperty("libraries").GetProperty(identity).GetRawText()));
            var subset = new JsonObject
            {
                ["packageFolders"] = JsonNode.Parse(actual.RootElement.GetProperty("packageFolders").GetRawText()),
                ["libraries"] = libraries
            };
            var selectingInput = Path.Combine(ownedRoot, "resolved-test-feed.assets.json");
            await File.WriteAllTextAsync(selectingInput, subset.ToJsonString(), ct);
            var proof = await BuildInputProvenance.ReadPackagesAsync([selectingInput], [],
                (paths, token) => policy.ValidateAsync("test-feed", paths, token), ct);
            await proof.AssertCurrentAsync((paths, token) => policy.ValidateAsync("test-feed", paths, token), ct);
            var archives = proof.Inputs.Where(input => input.FilePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.AreEqual(selected.Count, archives.Length, "Every selected dependency needs its verified installed archive.");
            var copies = new List<PhysicalInputIdentity>();
            foreach (var archive in archives)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.Combine(feed, Path.GetFileName(archive.FilePath));
                await policy.ValidateAsync("test-feed", [destination], ct);
                File.Copy(archive.FilePath, destination);
                copies.Add(archive with { FilePath = destination });
            }
            await PhysicalInputIdentity.AssertCurrentAsync(copies, ct);
            await proof.AssertCurrentAsync((paths, token) => policy.ValidateAsync("test-feed", paths, token), ct);
            await PhysicalInputIdentity.AssertCurrentAsync([sourceIdentity], ct);
            return versions;
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Resolved test feed document cleanup failed.", primary, actual); }
    }
}
