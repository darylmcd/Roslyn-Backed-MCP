using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>One MSBuild static-graph protocol for physical path plus exact global-property context.</summary>
internal static class EvaluatedBuildGraph
{
    internal sealed record Entry(string ProjectPath, ImmutableDictionary<string, string> Globals)
    {
        public override string ToString() => $"BuildEntry {{ GlobalPropertyCount = {Globals.Count} }}";
    }

    internal sealed record Node(string ProjectPath, ImmutableDictionary<string, string> Globals,
        ImmutableArray<Node> Dependencies, bool IsOuterBuild, string? TargetFramework, string? TargetPath,
        MsBuildMetadataHelper.RestoreInputs Inputs, EvaluatedProjectGraph.BuildInputs BuildInputs, IReadOnlyList<string> ArtifactFiles, IReadOnlyList<string> DocumentFiles)
    {
        internal required CompilerProjectReferences CompilerReferences { get; init; }
        internal required BuildPathRoles.Evidence PathEvidence { get; init; }
        internal required NativeTaskRegistrations TaskRegistrations { get; init; }
        internal required string? GeneratedEditorConfigPath { get; init; }
        internal required string EvaluatedItems { get; init; }
        internal required string EvaluatedProperties { get; init; }
        internal required ImmutableArray<NativeBuildMembership.FileItem> FileItems { get; init; }
        public override string ToString() => $"BuildNode {{ GlobalPropertyCount = {Globals.Count}, DependencyCount = {Dependencies.Length} }}";
    }

    internal sealed record Graph(ImmutableArray<Node> Nodes, ImmutableArray<Node> EntryNodes)
    {
        public override string ToString() => $"BuildGraph {{ NodeCount = {Nodes.Length}, EntryCount = {EntryNodes.Length} }}";
    }

    internal static bool StructuredInputsEquivalent(Node left, Node right, CancellationToken ct)
    {
        var serializer = new NativeStructuredIdentity.Session(ct);
        return serializer.Serialize(new { left.GeneratedEditorConfigPath, left.CompilerReferences })
            == serializer.Serialize(new { right.GeneratedEditorConfigPath, right.CompilerReferences })
            && serializer.Serialize(left.Inputs) == serializer.Serialize(right.Inputs);
    }

    internal static Graph Source(Solution solution, CancellationToken ct) => Source(solution, ImmutableDictionary<string, byte[]>.Empty, ct);

    internal static Graph Source(Solution solution, IReadOnlyDictionary<string, byte[]> definitions, CancellationToken ct)
    {
        var globals = EvaluatedProjectGraph.Properties(solution).ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        var roots = !string.IsNullOrWhiteSpace(solution.FilePath)
            ? new[] { solution.FilePath! }
            : solution.GetProjectDependencyGraph().GetTopologicallySortedProjects()
                .Where(id => !solution.GetProjectDependencyGraph().GetProjectsThatDirectlyDependOnThisProject(id).Any())
                .Select(id => solution.GetProject(id)?.FilePath
                    ?? throw new PublicInvalidOperationException("An evaluated graph root has no physical project path.")).ToArray();
        if (roots.Length == 0) throw new PublicInvalidOperationException("The loaded solution has no evaluated graph entry point.");
        return FromRoots(roots!, globals, definitions, ct);
    }

    internal static Graph FromRoots(IEnumerable<string> paths, IReadOnlyDictionary<string, string> globals,
        IReadOnlyDictionary<string, byte[]> definitions, CancellationToken ct) =>
        FromEntries(paths.Distinct(FileSystemPath.Comparer)
            .Select(path => new Entry(Path.GetFullPath(path), globals.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase))), definitions, ct);

    internal static Graph FromEntries(IEnumerable<Entry> entries, IReadOnlyDictionary<string, byte[]> definitions, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadCore(entries.ToArray(), definitions, originalSolutionPath: null, ct);
    }

    internal static Graph FromSolutionDiscovery(string discoveryPath, string originalSolutionPath,
        IReadOnlyDictionary<string, string> globals, IReadOnlyDictionary<string, byte[]> definitions, CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadCore([new(discoveryPath, globals.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase))], definitions, originalSolutionPath, ct);
    }

    internal static IEnumerable<Node> ActiveEntries(Graph graph) => graph.EntryNodes.SelectMany(entry => entry.IsOuterBuild
        ? entry.Dependencies.Where(node => !node.IsOuterBuild && FileSystemPath.Comparer.Equals(node.ProjectPath, entry.ProjectPath))
        : new[] { entry }).Distinct();

    internal static Node Resolve(Project project, CancellationToken ct) => Resolve(project, Source(project.Solution, ct));

    internal static Node[] MatchingContexts(Project project, Graph graph)
    {
        var path = project.FilePath ?? throw new PublicInvalidOperationException("A loaded project has no physical path.");
        var candidates = graph.Nodes.Where(node => !node.IsOuterBuild &&
            FileSystemPath.Comparer.Equals(node.ProjectPath, Path.GetFullPath(path))).ToArray();
        if (project.OutputFilePath is { Length: > 0 } output)
            candidates = candidates.Where(node => node.TargetPath is not null &&
                FileSystemPath.Comparer.Equals(node.TargetPath, Path.GetFullPath(output))).ToArray();
        return candidates;
    }

    internal static Node Resolve(Project project, Graph graph)
    {
        var candidates = MatchingContexts(project, graph);
        if (candidates.Length != 1) throw new PublicInvalidOperationException(
            "The loaded project does not identify one evaluated build context. Configure a distinct output path or select one target framework, then reload.");
        return candidates[0];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Graph ReadCore(Entry[] entries, IReadOnlyDictionary<string, byte[]> definitions, string? originalSolutionPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var conditionEvidence = new NativeConditionEvidence();
        Exception? primary = null;
        try { return ReadOwnedCore(entries, definitions, originalSolutionPath, conditionEvidence, ct); }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native graph observation cleanup failed.", primary, conditionEvidence); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Graph ReadOwnedCore(Entry[] entries, IReadOnlyDictionary<string, byte[]> definitions, string? originalSolutionPath,
        NativeConditionEvidence conditionEvidence, CancellationToken ct)
    {
        var collection = conditionEvidence.Collection;
        collection.RegisterLogger(conditionEvidence);
        try
        {
            conditionEvidence.VerifyProtocol(collection);
            // FullPath registers each unsaved root in this private collection's XML cache.
            // Imports therefore see the reviewed bytes without writing to the source tree.
            var overlays = new Dictionary<string, Microsoft.Build.Construction.ProjectRootElement>(FileSystemPath.Comparer);
            foreach (var (path, bytes) in definitions)
            {
                ct.ThrowIfCancellationRequested();
                using var text = new StringReader(CsprojSemanticEquality.CreateSnapshot(bytes).Content);
                using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var root = Microsoft.Build.Construction.ProjectRootElement.Create(reader, collection, preserveFormatting: true);
                root.FullPath = Path.GetFullPath(path);
                overlays.Add(root.FullPath, root);
            }
            var evidence = new System.Collections.Concurrent.ConcurrentDictionary<Microsoft.Build.Execution.ProjectInstance, BuildPathRoles.Evidence>();
            var registrations = new System.Collections.Concurrent.ConcurrentDictionary<Microsoft.Build.Execution.ProjectInstance, NativeTaskRegistrations>();
            var memberships = new System.Collections.Concurrent.ConcurrentDictionary<Microsoft.Build.Execution.ProjectInstance, NativeBuildMembership.Evaluation>();
            var generatedConfigurations = new Dictionary<Microsoft.Build.Execution.ProjectInstance, string?>();
            var evaluationLock = new object();
            var protocolMarker = "RoslynMcpGraphProtocol" + Guid.NewGuid().ToString("N");
            // Native evaluation and its condition observer already share one serialized factory.
            // Run graph work on this caller: extra graph workers otherwise contend on that
            // lock while the native scheduler can spin waiting for their pending work.
            var graph = new Microsoft.Build.Graph.ProjectGraph(entries.Select(entry =>
            {
                var properties = entry.Globals.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                // Static graph normally injects IsGraphBuild=true. It is an interpretation
                // protocol here, not permission to change the loaded non-graph evaluation.
                properties.TryAdd("IsGraphBuild", protocolMarker);
                return new Microsoft.Build.Graph.ProjectGraphEntryPoint(entry.ProjectPath, properties);
            }), collection, (path, globals, owner) =>
            {
                var properties = globals.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                if (properties.TryGetValue("IsGraphBuild", out var value) && value == protocolMarker) properties.Remove("IsGraphBuild");
                if (originalSolutionPath is not null)
                {
                    // GraphBuilder derives these from the isolated solution location even when
                    // supplied as globals. Discovery retains the original solution coordinates.
                    properties["SolutionDir"] = Microsoft.Build.Evaluation.ProjectCollection.Escape(Path.GetDirectoryName(originalSolutionPath)! + Path.DirectorySeparatorChar);
                    properties["SolutionPath"] = Microsoft.Build.Evaluation.ProjectCollection.Escape(originalSolutionPath);
                }
                lock (evaluationLock)
                {
                    conditionEvidence.Begin();
                    var evaluated = Microsoft.Build.Evaluation.Project.FromProjectRootElement(
                        Microsoft.Build.Construction.ProjectRootElement.Open(path, owner, preserveFormatting: true),
                        conditionEvidence.Options(properties, owner));
                    try
                    {
                        conditionEvidence.Bind(evaluated);
                        var paths = BuildPathRoles.Capture(evaluated, conditionEvidence);
                        var instance = evaluated.CreateProjectInstance();
                        generatedConfigurations.Add(instance, EvaluatedProjectGraph.CaptureGeneratedEditorConfigPath(evaluated));
                        evidence[instance] = paths;
                        registrations[instance] = NativeTaskRegistrations.Capture(instance, ct);
                        memberships[instance] = NativeBuildMembership.CaptureEvaluation(instance, ct);
                        return instance;
                    }
                    finally { owner.UnloadProject(evaluated); }
                }
            }, degreeOfParallelism: 1, cancellationToken: ct);
            var compilerContexts = CompilerReferenceContextGraph.ValidateContexts(
                graph.ProjectNodes.Select(node => node.ProjectInstance).ToArray(), instance => evidence[instance],
                instance => registrations[instance], ct);
            var nodes = new Dictionary<Microsoft.Build.Graph.ProjectGraphNode, Node>();
            var identities = new Dictionary<(string Path, ImmutableDictionary<string, string> Globals), Node>(ContextIdentityComparer.Instance);
            foreach (var source in graph.ProjectNodesTopologicallySorted)
            {
                ct.ThrowIfCancellationRequested();
                var instance = source.ProjectInstance;
                // MSBuild global-property dictionaries carry escaped values. Preserve that
                // representation for reevaluation; decoding here changes nested %XX values.
                var globals = instance.GlobalProperties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
                var inputs = MsBuildMetadataHelper.ReadRestoreInputs(instance.FullPath, instance.GetPropertyValue,
                    name => instance.GetItems(name).Select(item => new MsBuildMetadataHelper.EvaluatedItem(item.EvaluatedInclude, item.GetMetadataValue)));
                var innerName = instance.GetPropertyValue("InnerBuildProperty");
                var innerValuesName = instance.GetPropertyValue("InnerBuildPropertyValues");
                var outer = !string.IsNullOrWhiteSpace(innerName) && string.IsNullOrWhiteSpace(instance.GetPropertyValue(innerName))
                    && !string.IsNullOrWhiteSpace(innerValuesName) && !string.IsNullOrWhiteSpace(instance.GetPropertyValue(innerValuesName));
                var target = instance.GetPropertyValue("TargetPath");
                var immutableRoots = BuildInputProvenance.ToolRoots();
                var buildInputs = new EvaluatedProjectGraph.BuildInputs(instance.ImportPaths.Append(instance.FullPath)
                    .Distinct(FileSystemPath.Comparer).ToArray(), immutableRoots);
                var artifacts = instance.GetItems("Analyzer").Where(item => !string.IsNullOrWhiteSpace(item.EvaluatedInclude))
                    .Select(item => item.GetMetadataValue("FullPath"))
                    .Concat(instance.GetItems("Reference").Select(item => item.GetMetadataValue("HintPath"))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => Path.GetFullPath(value, Path.GetDirectoryName(instance.FullPath)!)))
                    .Distinct(FileSystemPath.Comparer).ToArray();
                var documents = new[] { "Compile", "AdditionalFiles", "EditorConfigFiles" }.SelectMany(name => instance.GetItems(name))
                    .Where(item => !string.IsNullOrWhiteSpace(item.EvaluatedInclude))
                    .Select(item => item.GetMetadataValue("FullPath")).Distinct(FileSystemPath.Comparer).ToArray();
                var node = new Node(Path.GetFullPath(instance.FullPath), globals,
                    source.ProjectReferences.Select(reference => nodes[reference]).Distinct().ToImmutableArray(), outer,
                    string.IsNullOrWhiteSpace(inputs.TargetFramework) ? null : inputs.TargetFramework,
                    string.IsNullOrWhiteSpace(target) ? null : NativeItemPath.FullPath(target, Path.GetDirectoryName(instance.FullPath)!), inputs, buildInputs, artifacts, documents)
                {
                    GeneratedEditorConfigPath = generatedConfigurations[instance],
                    PathEvidence = evidence[instance],
                    TaskRegistrations = registrations[instance],
                    EvaluatedItems = memberships[instance].Items,
                    EvaluatedProperties = memberships[instance].Properties,
                    FileItems = memberships[instance].Files,
                    CompilerReferences = CompilerProjectReferences.Capture(instance,
                        CompilerReferenceContextGraph.Capture(instance, compilerContexts, ct))
                };
                var identity = (node.ProjectPath, node.Globals);
                if (identities.TryGetValue(identity, out var existing))
                {
                    if (node.IsOuterBuild != existing.IsOuterBuild || node.TargetPath != existing.TargetPath ||
                        !node.Dependencies.ToHashSet().SetEquals(existing.Dependencies) ||
                        !node.BuildInputs.Definitions.ToHashSet(FileSystemPath.Comparer).SetEquals(existing.BuildInputs.Definitions) ||
                        !node.BuildInputs.ImmutableRoots.ToHashSet(FileSystemPath.Comparer).SetEquals(existing.BuildInputs.ImmutableRoots) ||
                        !node.ArtifactFiles.ToHashSet(FileSystemPath.Comparer).SetEquals(existing.ArtifactFiles) ||
                        !node.DocumentFiles.ToHashSet(FileSystemPath.Comparer).SetEquals(existing.DocumentFiles) ||
                        !node.PathEvidence.EquivalentTo(existing.PathEvidence) ||
                        !node.TaskRegistrations.Equals(existing.TaskRegistrations) || node.EvaluatedItems != existing.EvaluatedItems || node.EvaluatedProperties != existing.EvaluatedProperties ||
                        !StructuredInputsEquivalent(node, existing, ct))
                        throw InvalidOperationErrors.Internal("The same build identity evaluated inconsistently.");
                    node = existing;
                }
                else identities.Add(identity, node);
                nodes.Add(source, node);
            }
            GC.KeepAlive(overlays);
            return new Graph(graph.ProjectNodesTopologicallySorted.Select(node => nodes[node]).Distinct().ToImmutableArray(),
                graph.EntryPointNodes.Select(node => nodes[node]).Distinct().ToImmutableArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // ProjectGraph can aggregate cancellation from its parallel evaluators.
            ct.ThrowIfCancellationRequested();
            var causes = exception is AggregateException grouped ? grouped.Flatten().InnerExceptions.ToArray() : new[] { exception };
            if (causes.OfType<NativeConditionProtocolException>().FirstOrDefault() is { } protocol) throw protocol;
            if (causes.OfType<NativeTaskRegistryUnavailableException>().FirstOrDefault() is { } registry) throw registry;
            // Evaluation may echo arbitrary captured values in exception messages or inner chains.
            var failure = new PublicInvalidOperationException("The configured build graph could not be evaluated (" + exception.GetType().Name + ").");
            var errors = exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : new[] { exception };
            failure.Data["BuildGraphFailureTypes"] = string.Join(",", errors.Select(error => error switch
            {
                Microsoft.Build.Exceptions.InvalidProjectFileException => "InvalidProjectFileException",
                NullReferenceException => "NullReferenceException",
                FileNotFoundException => "FileNotFoundException",
                TypeLoadException => "TypeLoadException",
                MissingMethodException => "MissingMethodException",
                InvalidOperationException => "InvalidOperationException",
                ArgumentException => "ArgumentException",
                IOException => "IOException",
                UnauthorizedAccessException => "UnauthorizedAccessException",
                _ => "Other"
            }).Distinct(StringComparer.Ordinal));
            failure.Data["BuildGraphErrorCodes"] = string.Join(",", errors.OfType<Microsoft.Build.Exceptions.InvalidProjectFileException>()
                .Select(error => error.ErrorCode).Where(code => code is { Length: 7 } && code.StartsWith("MSB", StringComparison.Ordinal)
                    && code.AsSpan(3).ToString().All(char.IsAsciiDigit)).Distinct(StringComparer.Ordinal));
            failure.Data["BuildGraphFailureStages"] = errors.Any(error => error.StackTrace?.Contains("BuildPathRoles", StringComparison.Ordinal) == true)
                ? "PathEvidence" : "NativeEvaluation";
            var knownSymbols = new[] { "xmlns", "Project", "PropertyGroup", "Import", "Sdk", "Condition", "ProjectAssetsFile", "ContentPreprocessorOutputDirectory", "TargetPath", "MSBuildProjectExtensionsPath" };
            failure.Data["BuildGraphXmlFailureSymbols"] = string.Join(",", knownSymbols.Where(symbol => errors.OfType<Microsoft.Build.Exceptions.InvalidProjectFileException>()
                .Any(error => error.Message.Contains("\"" + symbol + "\"", StringComparison.Ordinal)
                    || error.Message.Contains("'" + symbol + "'", StringComparison.Ordinal)
                    || error.Message.Contains("<" + symbol + ">", StringComparison.Ordinal))));
            throw failure;
        }
    }
    internal sealed class ContextIdentityComparer : IEqualityComparer<(string Path, ImmutableDictionary<string, string> Globals)>
    {
        internal static ContextIdentityComparer Instance { get; } = new();
        public bool Equals((string Path, ImmutableDictionary<string, string> Globals) left,
            (string Path, ImmutableDictionary<string, string> Globals) right) =>
            FileSystemPath.Comparer.Equals(left.Path, right.Path) && left.Globals.Count == right.Globals.Count &&
            left.Globals.All(pair => right.Globals.TryGetValue(pair.Key, out var value) && pair.Value == value);
        public int GetHashCode((string Path, ImmutableDictionary<string, string> Globals) identity)
        {
            var hash = new HashCode();
            hash.Add(identity.Path, FileSystemPath.Comparer);
            foreach (var pair in identity.Globals.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(pair.Key, StringComparer.OrdinalIgnoreCase);
                hash.Add(pair.Value, StringComparer.Ordinal);
            }
            return hash.ToHashCode();
        }
    }

}
