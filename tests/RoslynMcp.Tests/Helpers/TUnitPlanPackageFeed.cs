using System.Text;
using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Offline package-plan fixtures; NativeMtpExecutionTests qualifies actual framework execution.</summary>
internal static class TUnitPlanPackageFeed
{
    internal static async Task RestoreAsync(string root, string loadedPath, bool engine, IDotnetCommandRunner runner)
    {
        var feed = Path.Combine(root, "plan-feed");
        Directory.CreateDirectory(feed);
        var dependencies = new XElement("dependencies",
            new XElement("dependency", new XAttribute("id", "Microsoft.Testing.Extensions.TrxReport"), new XAttribute("version", "[2.0.0]")));
        if (engine) dependencies.Add(new XElement("dependency", new XAttribute("id", "TUnit.Engine"), new XAttribute("version", "[1.65.38]")));
        await PackageAsync("TUnit", "1.65.38", dependencies, new Dictionary<string, byte[]>
        {
            ["buildTransitive/TUnit.props"] = Encoding.UTF8.GetBytes("<Project><PropertyGroup><IsTestProject>true</IsTestProject><IsTestingPlatformApplication>true</IsTestingPlatformApplication></PropertyGroup></Project>")
        });
        await PackageAsync("TUnit.Engine", "1.65.38", null, new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        await PackageAsync("Microsoft.Testing.Extensions.TrxReport", "2.0.0", null, new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
        await PackageAsync("Microsoft.NET.Test.Sdk", "17.11.1", null, new Dictionary<string, byte[]>
        {
            ["buildTransitive/Microsoft.NET.Test.Sdk.props"] = Encoding.UTF8.GetBytes("<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>")
        });
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.targets"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(root, "NuGet.Config"), new XElement("configuration",
            new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "owned"), new XAttribute("value", feed))),
            new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", Path.Combine(root, "plan-cache"))))).ToString());
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (!XDocument.Load(project).Descendants("PackageReference").Any(item => (string?)item.Attribute("Include") == "TUnit")) continue;
            // The fake executor never runs a framework. These authored symbols exercise semantic discovery only.
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(project)!, "PlanAttribute.cs"),
                "namespace TUnit.Core { public sealed class TestAttribute : System.Attribute { } } internal static class PlanEntry { static void Main() { } }");
        }
        var global = Path.Combine(root, "global.json");
        if (!File.Exists(global)) await File.WriteAllTextAsync(global, "{}");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var properties = OwnedNativeLoadContext.Properties(loadedPath, new Dictionary<string, string>());
        var result = await runner.RunAsync(root, loadedPath, MsBuildCommandProperties.Append(
            ["restore", loadedPath, "--nologo", "--source", feed, "--packages", Path.Combine(root, "plan-cache")], properties), deadline.Token);
        Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);

        Task PackageAsync(string id, string version, XElement? packageDependencies, IReadOnlyDictionary<string, byte[]> files)
        {
            var metadata = new XElement("metadata", new XElement("id", id), new XElement("version", version),
                new XElement("authors", "Fixture"), new XElement("description", "Owned protocol-plan fixture"));
            if (packageDependencies is not null) metadata.Add(packageDependencies);
            var contents = new Dictionary<string, byte[]>(files) { [id + ".nuspec"] = Encoding.UTF8.GetBytes(new XElement("package", metadata).ToString()) };
            return NuGetInstallationFixture.WriteArchiveAsync(Path.Combine(feed, id + "." + version + ".nupkg"), contents);
        }
    }
}
