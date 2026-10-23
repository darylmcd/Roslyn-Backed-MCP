using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Freezes actual evaluated execution contexts before physical publication; never prints captured values.</summary>
internal sealed class EvaluatedExecutionIdentity
{
    private readonly string[] _entries;
    private readonly string[] _nodes;
    private readonly ImmutableHashSet<string> _pathProperties;
    private EvaluatedExecutionIdentity(string[] entries, string[] nodes, ImmutableHashSet<string> pathProperties)
    {
        _entries = entries;
        _nodes = nodes;
        _pathProperties = pathProperties;
    }

    public override string ToString() => $"EvaluatedExecutionIdentity {{ EntryCount = {_entries.Length}, NodeCount = {_nodes.Length} }}";

    internal static EvaluatedExecutionIdentity Capture(Solution solution, Func<string, string> mapPath,
        IReadOnlySet<string> pathProperties, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (solution.ProjectIds.Count == 0) return new([], [], pathProperties.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
        var graph = EvaluatedBuildGraph.Source(solution, ImmutableDictionary<string, byte[]>.Empty, ct);
        return Capture(graph, mapPath, pathProperties, ct);
    }

    internal static EvaluatedExecutionIdentity Capture(EvaluatedBuildGraph.Graph graph, Func<string, string> mapPath,
        IReadOnlySet<string> pathProperties, CancellationToken ct)
    {
        var serializer = new NativeStructuredIdentity.Session(ct);
        var paths = pathProperties.Concat(new[] { "SolutionDir", "SolutionPath" })
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        string PathKey(string? path) => path is null ? "" : NormalizePath(mapPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))));
        string PropertyValue(string name, string value)
        {
            if (name.Equals("CurrentSolutionConfigurationContents", StringComparison.OrdinalIgnoreCase))
                return BuildPathRelocation.RelocateSolutionConfiguration(value, path => PathKey(path));
            if (!paths.Contains(name) || BuildPathRelocation.IsPackageProperty(name)) return value;
            return string.Join(";", value.Split(';').Select(token =>
            {
                var decoded = Microsoft.Build.Evaluation.ProjectCollection.Unescape(token);
                return Path.IsPathFullyQualified(decoded)
                    ? Microsoft.Build.Evaluation.ProjectCollection.Escape(PathKey(decoded)) : token;
            }));
        }
        string ContextKey(EvaluatedBuildGraph.Node node) => serializer.Serialize(new
        {
            Path = PathKey(node.ProjectPath),
            Globals = node.Globals.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { Name = pair.Key.ToUpperInvariant(), Value = PropertyValue(pair.Key, pair.Value) }).ToArray()
        });
        var nodes = new List<string>();
        foreach (var node in graph.Nodes)
        {
            ct.ThrowIfCancellationRequested();
            var inputs = node.Inputs;
            nodes.Add(serializer.Serialize(new
            {
                Context = ContextKey(node),
                node.IsOuterBuild,
                NodeTargetFramework = node.TargetFramework,
                TargetPath = PathKey(node.TargetPath),
                GeneratedEditorConfig = PathKey(node.GeneratedEditorConfigPath),
                OutputDirectories = node.PathEvidence.Directories.Select(PathKey).Order(StringComparer.Ordinal).ToArray(),
                OutputFiles = node.PathEvidence.OutputFiles.Select(PathKey).Order(StringComparer.Ordinal).ToArray(),
                NativeInputDirectories = node.PathEvidence.NativeInputDirectories.Select(PathKey).Order(StringComparer.Ordinal).ToArray(),
                Assets = PathKey(inputs.AssetsPath),
                Extensions = PathKey(inputs.ExtensionsPath),
                inputs.TargetFramework,
                inputs.TargetFrameworks,
                inputs.TargetFrameworkMoniker,
                inputs.RuntimeIdentifier,
                inputs.IsTestProject,
                inputs.IsTestingPlatformApplication,
                inputs.EnableMSTestRunner,
                inputs.EnableNUnitRunner,
                inputs.UseMicrosoftTestingPlatformRunner,
                inputs.UseVSTest,
                inputs.OutputType,
                CompilerProjectReferences = new
                {
                    node.CompilerReferences.DisableTransitive,
                    node.CompilerReferences.Defaults,
                    node.CompilerReferences.NativeCompileTargets,
                    Declared = node.CompilerReferences.Declared.Select(item => new
                    {
                        Path = PathKey(item.Path),
                        item.Metadata,
                        Targets = item.Targets.Select(target => new
                        {
                            Path = PathKey(target.Path),
                            Output = PathKey(target.Output),
                            target.Framework,
                            Globals = target.Globals.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(pair => new { Name = pair.Key.ToUpperInvariant(), Value = PropertyValue(pair.Key, pair.Value) }).ToArray()
                        }).OrderBy(target => target.Path, StringComparer.Ordinal).ThenBy(target => target.Output, StringComparer.Ordinal).ToArray()
                    })
                        .OrderBy(item => item.Path, StringComparer.Ordinal).ToArray()
                },
                Packages = inputs.Packages.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(package => package.RequestedVersion, StringComparer.Ordinal).ThenBy(package => package.UsesCentralVersion).ToArray(),
                Dependencies = node.Dependencies.Select(ContextKey).Order(StringComparer.Ordinal).ToArray()
            }));
        }
        return new(graph.EntryNodes.Select(ContextKey).Order(StringComparer.Ordinal).ToArray(),
            nodes.Order(StringComparer.Ordinal).ToArray(), paths);
    }

    internal void AssertEquivalent(Solution actual, CancellationToken ct)
    {
        var candidate = Capture(actual, static path => path, _pathProperties, ct);
        if (!_entries.SequenceEqual(candidate._entries, StringComparer.Ordinal) || !_nodes.SequenceEqual(candidate._nodes, StringComparer.Ordinal))
        {
            var failure = new PhysicalGraphMismatchException("The evaluated execution graph differs from the staged authority.");
            failure.Data["ExecutionEntryCounts"] = _entries.Length + "/" + candidate._entries.Length;
            failure.Data["ExecutionNodeCounts"] = _nodes.Length + "/" + candidate._nodes.Length;
            var components = new List<string>();
            if (!_entries.SequenceEqual(candidate._entries, StringComparer.Ordinal)) components.Add("EntryContexts");
            // Compare component multisets solely to classify a mismatch. These private
            // serialized values never enter exception messages, Data, or logging.
            foreach (var name in new[] { "Context", "IsOuterBuild", "NodeTargetFramework", "TargetPath", "GeneratedEditorConfig", "OutputDirectories", "OutputFiles", "NativeInputDirectories", "Assets", "Extensions",
                         "TargetFramework", "TargetFrameworks", "TargetFrameworkMoniker", "RuntimeIdentifier", "IsTestProject",
                         "IsTestingPlatformApplication", "EnableMSTestRunner", "EnableNUnitRunner", "UseMicrosoftTestingPlatformRunner",
                         "UseVSTest", "OutputType", "CompilerProjectReferences", "Packages", "Dependencies" })
            {
                ct.ThrowIfCancellationRequested();
                if (!ComponentValues(_nodes, name).SequenceEqual(ComponentValues(candidate._nodes, name), StringComparer.Ordinal))
                    components.Add(name);
            }
            failure.Data["ExecutionIdentityMismatchComponents"] = string.Join(",", components);
            failure.Data["ExecutionContextMismatchComponents"] = ContextMismatchComponents(_entries, candidate._entries, ct);
            throw failure;
        }
    }

    private static string ContextMismatchComponents(string[] expected, string[] actual, CancellationToken ct)
    {
        static string[] Values(string[] contexts, string field) => contexts.Select(context =>
        {
            using var document = JsonDocument.Parse(context);
            return document.RootElement.GetProperty(field).GetRawText();
        }).Order(StringComparer.Ordinal).ToArray();
        static Dictionary<string, string[]> Globals(string[] contexts)
        {
            var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var context in contexts)
            {
                using var document = JsonDocument.Parse(context);
                foreach (var property in document.RootElement.GetProperty("Globals").EnumerateArray())
                {
                    var name = property.GetProperty("Name").GetString()!;
                    if (!values.TryGetValue(name, out var list)) values.Add(name, list = []);
                    list.Add(property.GetProperty("Value").GetString()!);
                }
            }
            return values.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        }
        var differences = new List<string>();
        if (!Values(expected, "Path").SequenceEqual(Values(actual, "Path"), StringComparer.Ordinal)) differences.Add("ProjectPath");
        var left = Globals(expected);
        var right = Globals(actual);
        if (!left.Keys.Order(StringComparer.Ordinal).SequenceEqual(right.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal)) differences.Add("GlobalNameSet");
        // Labels are a fixed public MSBuild protocol inventory, never captured names.
        var known = new[] { "CurrentSolutionConfigurationContents", "SolutionDir", "SolutionPath", "SolutionName", "SolutionFileName",
            "SolutionExt", "Configuration", "Platform", "TargetFramework", "DesignTimeBuild", "BuildingInsideVisualStudio",
            "BuildProjectReferences", "IsGraphBuild", "VisualStudioVersion" };
        var unknownDifferences = 0;
        foreach (var name in left.Keys.Union(right.Keys, StringComparer.Ordinal))
        {
            if (left.TryGetValue(name, out var before) && right.TryGetValue(name, out var after) && before.SequenceEqual(after, StringComparer.Ordinal)) continue;
            var label = known.FirstOrDefault(candidate => string.Equals(
                NativeStructuredIdentity.StringToken(candidate.ToUpperInvariant(), ct), name, StringComparison.Ordinal));
            if (label is not null) differences.Add("GlobalValue:" + label);
            else unknownDifferences++;
        }
        if (unknownDifferences > 0) differences.Add("OtherGlobalDifferences:" + unknownDifferences);
        return string.Join(",", differences);
    }

    private static string[] ComponentValues(IEnumerable<string> nodes, string name)
    {
        var values = new List<string>();
        foreach (var node in nodes)
        {
            using var document = JsonDocument.Parse(node);
            values.Add(document.RootElement.GetProperty(name).GetRawText());
        }
        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizePath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }
}
