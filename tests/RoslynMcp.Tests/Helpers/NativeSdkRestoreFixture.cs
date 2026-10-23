using System.Collections.Immutable;
using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Real SDK project/settings premises with independently configured exact native configuration grants.</summary>
internal static class NativeSdkRestoreFixture
{
    internal static void InitializeDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "global.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
    }

    internal static void WriteProject(string path, string definition) => File.WriteAllText(path, SdkDefinition(definition));
    internal static Task WriteProjectAsync(string path, string definition, CancellationToken ct) =>
        File.WriteAllTextAsync(path, SdkDefinition(definition), ct);

    private static string SdkDefinition(string definition)
    {
        var xml = XDocument.Parse(definition, LoadOptions.PreserveWhitespace);
        var root = xml.Root ?? throw new AssertFailedException("The native fixture requires a project definition.");
        Assert.AreEqual("Project", root.Name.LocalName);
        if (root.Attribute("Sdk") is null) root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
        var properties = root.Element("PropertyGroup");
        if (properties is null) { properties = new XElement("PropertyGroup"); root.AddFirst(properties); }
        if (!root.Descendants("TargetFramework").Any() && !root.Descendants("TargetFrameworks").Any())
            properties.Add(new XElement("TargetFramework", "net10.0"));
        if (!root.Descendants("NuGetAudit").Any()) properties.Add(new XElement("NuGetAudit", "false"));
        if (root.Descendants("Compile").Any(item => item.Attribute("Include") is not null)
            && !root.Descendants("EnableDefaultCompileItems").Any())
            properties.Add(new XElement("EnableDefaultCompileItems", "false"));
        return xml.ToString(SaveOptions.DisableFormatting);
    }

    internal static async Task<IWorkspacePathPolicy> CreatePolicyAsync(string projectPath, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var graph = EvaluatedBuildGraph.FromRoots([projectPath], ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, byte[]>.Empty, ct);
        var configured = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var node in graph.Nodes.Where(node => !node.IsOuterBuild))
        {
            var (capture, _) = await NativeCompilerTaskInputs.ReadRawAsync(node.ProjectPath, node.Globals, node.TargetFramework,
                node.TaskRegistrations.ToolsVersion, ["_GetRestoreSettings"], NativeCompilerTaskInputs.CapturePurpose.RestoreSettings, ct);
            var settings = capture.RestoreSettings.Single(receipt => FileSystemPath.Comparer.Equals(receipt.Project, node.ProjectPath));
            var selected = settings.Parameters.GetValueOrDefault("OutputConfigFilePaths") ?? [];
            Assert.IsTrue(selected.All(Path.IsPathFullyQualified), "Native selected configurations must have exact physical identities.");
            configured.UnionWith(selected);
            var names = settings.Parameters["NativeSupportedConfigNames"];
            for (var directory = Path.GetDirectoryName(node.ProjectPath); directory is not null; directory = Path.GetDirectoryName(directory))
                foreach (var name in names) configured.Add(Path.Combine(directory, name));
        }
        return new ExactConfigurationPolicy(configured);
    }

    private sealed class ExactConfigurationPolicy(IReadOnlySet<string> configurations) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(workspaceId, paths.Where(path => !configurations.Contains(path)).ToArray(), ct);
            foreach (var path in paths.Where(configurations.Contains))
            {
                ct.ThrowIfCancellationRequested();
                Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.GetFullPath(path), PhysicalPathResolver.Resolve(path)),
                    "Native configuration grants never admit physical aliases.");
            }
        }
    }
}
