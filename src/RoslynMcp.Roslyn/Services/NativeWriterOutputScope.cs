using System.Collections.Immutable;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Original declaration scope; recognizing a fact here grants no output authority.</summary>
internal sealed class NativeWriterOutputScope
{
    private readonly EvaluatedBuildGraph.Node _node;
    private readonly HashSet<string> _protected;
    private readonly string _directory;
    private readonly string _context;

    internal NativeWriterOutputScope(EvaluatedBuildGraph.Graph graph, EvaluatedBuildGraph.Node node,
        ImmutableHashSet<string> protectedInputs, CancellationToken ct)
    {
        _node = node;
        _directory = Path.GetDirectoryName(node.ProjectPath)!;
        _context = NativeTaskExecutionIdentity.EscapedContextIdentity(NativeCompilerTaskInputs.EffectiveGlobals(node.Globals, node.TargetFramework), node.TaskRegistrations.ToolsVersion, ct);
        _protected = NativeOutputPreparation.ProtectedInputPaths(graph, protectedInputs);
    }

    internal bool TryGetPath(NativeOutputProducerObserver.Receipt receipt, out string path)
    {
        var resolvedPath = Path.GetFullPath(receipt.Path, _directory);
        path = resolvedPath;
        return NativeOutputProducerObserver.CanonicalTask(receipt.Task) is not null
            && NativeOutputProducerObserver.OutputParameter(receipt.Task) == receipt.Parameter
            && FileSystemPath.Comparer.Equals(Path.GetFullPath(receipt.Project, _directory), _node.ProjectPath)
            && receipt.Target.Length != 0
            && _node.BuildInputs.Definitions.Contains(Path.GetFullPath(receipt.TargetFile, _directory), FileSystemPath.Comparer)
            && !_protected.Contains(resolvedPath)
            && (_node.PathEvidence.OutputFiles.Contains(resolvedPath, FileSystemPath.Comparer)
                || _node.PathEvidence.Directories.Any(output => FileSystemPath.IsStrictDescendant(output, resolvedPath)));
    }

    internal bool HasVerifiedImplementation(NativeOutputProducerObserver.Receipt receipt,
        ImmutableDictionary<string, PhysicalInputIdentity> assemblies)
    {
        var name = NativeOutputProducerObserver.CanonicalTask(receipt.Task);
        return name is not null && receipt.ContextIdentity == _context
            && receipt.Implementation == (name switch
            {
                "WriteCodeFragment" => "Microsoft.Build.Tasks.WriteCodeFragment",
                "WriteLinesToFile" => "Microsoft.Build.Tasks.WriteLinesToFile",
                "GenerateMSBuildEditorConfig" => "Microsoft.CodeAnalysis.BuildTasks.GenerateMSBuildEditorConfig",
                "ResolvePackageAssets" => "Microsoft.NET.Build.Tasks.ResolvePackageAssets",
                _ => ""
            })
            && assemblies.TryGetValue(name, out var assembly)
            && FileSystemPath.Comparer.Equals(Path.GetFullPath(receipt.Assembly, _directory), assembly.FilePath);
    }
}
