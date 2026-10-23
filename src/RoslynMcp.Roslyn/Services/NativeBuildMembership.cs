using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Execution;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Frozen evaluated membership, rechecked within the trusted native evaluation boundary.</summary>
internal sealed class NativeBuildMembership(ImmutableArray<EvaluatedBuildGraph.Entry> entries, string identity, ImmutableArray<string> traversalRoots)
{
    private readonly string _identity = identity;
    internal sealed record FileItem(string Path, string Identity);
    internal sealed record Evaluation(string Items, string Properties, ImmutableArray<FileItem> Files);

    internal static Evaluation CaptureEvaluation(ProjectInstance instance, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var files = ImmutableArray.CreateBuilder<FileItem>();
        using var itemBytes = new MemoryStream();
        using var itemWriter = new BinaryWriter(itemBytes, Encoding.UTF8, leaveOpen: true);
        using var allBytes = new MemoryStream();
        using var allWriter = new BinaryWriter(allBytes, Encoding.UTF8, leaveOpen: true);
        allWriter.Write(instance.Items.Count);
        foreach (var item in instance.Items)
        {
            ct.ThrowIfCancellationRequested();
            itemBytes.SetLength(0);
            itemBytes.Position = 0;
            NativeIdentityEncoding.WriteString(itemWriter, item.ItemType, ct);
            NativeIdentityEncoding.WriteString(itemWriter, item.EvaluatedInclude, ct);
            var metadata = item.Metadata.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            itemWriter.Write(metadata.Length);
            foreach (var value in metadata)
            {
                NativeIdentityEncoding.WriteString(itemWriter, value.Name.ToUpperInvariant(), ct);
                NativeIdentityEncoding.WriteString(itemWriter, value.EvaluatedValue, ct);
            }
            itemWriter.Flush();
            var hash = SHA256.HashData(itemBytes.GetBuffer().AsSpan(0, checked((int)itemBytes.Length)));
            // Fixed-width hashes preserve native occurrence order and multiplicity.
            allWriter.Write(hash);
            if (!BuildPathRelocation.IsPathItem(item.ItemType)) continue;
            var include = item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase)
                ? item.GetMetadataValue("HintPath") : item.EvaluatedInclude;
            if (!string.IsNullOrWhiteSpace(include))
                files.Add(new(item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(include, Path.GetDirectoryName(instance.FullPath)!)
                    : item.GetMetadataValue("FullPath"), Convert.ToHexString(hash)));
        }
        allWriter.Flush();
        var identity = Convert.ToHexString(SHA256.HashData(allBytes.GetBuffer().AsSpan(0, checked((int)allBytes.Length))));
        ct.ThrowIfCancellationRequested();
        // Freeze the complete evaluated property context, including values inherited
        // from the environment. Compiler properties are extensible through imports;
        // an allowlist would miss indirect/custom property dependencies.
        allBytes.SetLength(0);
        allBytes.Position = 0;
        var properties = instance.Properties.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        allWriter.Write(properties.Length);
        foreach (var property in properties)
        {
            NativeIdentityEncoding.WriteString(allWriter, property.Name.ToUpperInvariant(), ct);
            NativeIdentityEncoding.WriteString(allWriter, property.EvaluatedValue, ct);
        }
        allWriter.Flush();
        var propertyIdentity = Convert.ToHexString(SHA256.HashData(allBytes.GetBuffer().AsSpan(0, checked((int)allBytes.Length))));
        ct.ThrowIfCancellationRequested();
        return new(identity, propertyIdentity, files.ToImmutable());
    }

    public override string ToString() => "NativeBuildMembership";

    internal static NativeBuildMembership Capture(EvaluatedBuildGraph.Graph graph, CancellationToken ct)
    {
        var serializer = new NativeStructuredIdentity.Session(ct);
        string Digest<T>(T value, CancellationToken token)
        {
            var encoded = serializer.Serialize(value);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded)));
            token.ThrowIfCancellationRequested();
            return hash;
        }
        var contexts = new Dictionary<EvaluatedBuildGraph.Node, string>(ReferenceEqualityComparer.Instance);
        string Context(EvaluatedBuildGraph.Node node)
        {
            ct.ThrowIfCancellationRequested();
            if (contexts.TryGetValue(node, out var existing)) return existing;
            var value = Digest(new
            {
                node.ProjectPath,
                Identity = NativeTaskExecutionIdentity.EscapedContextIdentity(node.Globals, node.TaskRegistrations.ToolsVersion, ct)
            }, ct);
            contexts.Add(node, value);
            return value;
        }
        var nodes = graph.Nodes.Select(node => Digest(new
        {
            Context = Context(node),
            node.IsOuterBuild,
            node.TargetFramework,
            node.TargetPath,
            node.GeneratedEditorConfigPath,
            node.EvaluatedItems,
            node.EvaluatedProperties,
            Definitions = node.BuildInputs.Definitions.Order(StringComparer.Ordinal).ToArray(),
            ImmutableRoots = node.BuildInputs.ImmutableRoots.Order(StringComparer.Ordinal).ToArray(),
            Documents = node.DocumentFiles.Order(StringComparer.Ordinal).ToArray(),
            Artifacts = node.ArtifactFiles.Order(StringComparer.Ordinal).ToArray(),
            RestoreInputs = node.Inputs,
            node.CompilerReferences,
            TaskRegistry = node.TaskRegistrations.Identity,
            TraversalRoots = node.PathEvidence.TraversalRoots.Order(StringComparer.Ordinal).ToArray(),
            Directories = node.PathEvidence.Directories.Order(StringComparer.Ordinal).ToArray(),
            Outputs = node.PathEvidence.OutputFiles.Order(StringComparer.Ordinal).ToArray(),
            OutputCoordinates = node.PathEvidence.OutputCoordinates.Order(StringComparer.Ordinal).ToArray(),
            NativeDirectories = node.PathEvidence.NativeInputDirectories.Order(StringComparer.Ordinal).ToArray(),
            Inputs = node.PathEvidence.InputFiles.Order(StringComparer.Ordinal).ToArray(),
            Coordinates = node.PathEvidence.InputCoordinates.Select(value => Digest(value, ct)).Order(StringComparer.Ordinal).ToArray(),
            Conditions = node.PathEvidence.ConditionProbes.Select(value => Digest(value, ct)).Order(StringComparer.Ordinal).ToArray(),
            Declarations = node.PathEvidence.Declarations.Select(value => Digest(value, ct)).Order(StringComparer.Ordinal).ToArray(),
            Dependencies = node.Dependencies.Select(Context).Order(StringComparer.Ordinal).ToArray()
        }, ct)).Order(StringComparer.Ordinal).ToArray();
        return new(graph.EntryNodes.Select(node => new EvaluatedBuildGraph.Entry(node.ProjectPath, node.Globals)).ToImmutableArray(),
            Digest(new { Entries = graph.EntryNodes.Select(Context).Order(StringComparer.Ordinal).ToArray(), Nodes = nodes }, ct),
            graph.Nodes.SelectMany(node => node.PathEvidence.TraversalRoots).Distinct(FileSystemPath.Comparer).ToImmutableArray());
    }

    internal async Task AssertTemporaryPathDisjointAsync(string path, IEnumerable<string> compilerTraversalRoots,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        var roots = traversalRoots.Concat(compilerTraversalRoots).Distinct(FileSystemPath.Comparer).ToArray();
        await AssertDisjointFromTraversalRootsAsync(path, roots, authorizePaths, ct).ConfigureAwait(false);
    }

    internal IReadOnlyList<string> RecordedTraversalRoots => traversalRoots;

    internal static async Task AssertDisjointFromTraversalRootsAsync(string path, IEnumerable<string> traversalRoots,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        if (!await IsDisjointFromTraversalRootsAsync(path, traversalRoots, authorizePaths, ct).ConfigureAwait(false))
            throw new PublicInvalidOperationException("Temporary staging requires a directory outside every source traversal.");
    }

    internal static async Task<bool> IsDisjointFromTraversalRootsAsync(string path, IEnumerable<string> traversalRoots,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorizePaths, CancellationToken ct)
    {
        var roots = traversalRoots.Distinct(FileSystemPath.Comparer).ToArray();
        await authorizePaths(roots, ct).ConfigureAwait(false);
        var temporary = PhysicalPathResolver.Resolve(path);
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            var input = PhysicalPathResolver.Resolve(root);
            if (FileSystemPath.Comparer.Equals(input, temporary)
                || FileSystemPath.IsStrictDescendant(input, temporary)
                || FileSystemPath.IsStrictDescendant(temporary, input))
                return false;
        }
        return true;
    }

    internal bool EquivalentTo(NativeBuildMembership other) => _identity == other._identity;

    internal void AssertCurrent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Native MSBuild evaluation is already trusted (ADR 0029). It may discover
        // paths, but a differing manifest is refused before our explicit IO can
        // read, copy or grant authority to newly discovered inputs.
        var actual = Capture(EvaluatedBuildGraph.FromEntries(entries, ImmutableDictionary<string, byte[]>.Empty, ct), ct);
        if (!EquivalentTo(actual))
            throw new PublicInvalidOperationException("Native build input membership changed after preparation; reload before executing the captured context.");
    }
}
