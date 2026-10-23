using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static partial class NativeOutputPreparation
{
    internal static async Task<ImmutableDictionary<string, PhysicalInputIdentity>> CaptureWriterAssembliesAsync(
        EvaluatedBuildGraph.Node node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sdk = NativeCompilerTaskInputs.RegisteredSdkRoot();
        var packageTask = node.CompilerReferences.MicrosoftNetBuildTasksAssembly;
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WriteCodeFragment"] = Path.Combine(sdk, "Microsoft.Build.Tasks.Core.dll"),
            ["WriteLinesToFile"] = Path.Combine(sdk, "Microsoft.Build.Tasks.Core.dll"),
            ["GenerateMSBuildEditorConfig"] = Path.Combine(sdk, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll")
        };
        if (packageTask is not null && Path.IsPathFullyQualified(packageTask) && Path.GetFileName(packageTask) == "Microsoft.NET.Build.Tasks.dll"
            && FileSystemPath.IsStrictDescendant(Path.Combine(sdk, "Sdks", "Microsoft.NET.Sdk", "tools"), packageTask))
            candidates.Add("ResolvePackageAssets", packageTask);
        var result = ImmutableDictionary.CreateBuilder<string, PhysicalInputIdentity>(StringComparer.Ordinal);
        var provenance = new ImmutablePathProvenance(node.BuildInputs.ImmutableRoots, [], [Path.GetDirectoryName(node.ProjectPath)!]);
        foreach (var pair in candidates)
        {
            // Independently admitted original SDK roots authorize these immutable
            // implementation reads; an authored UsingTask override cannot do so.
            if (!provenance.IsImmutable(pair.Value)) continue;
            var identity = await PhysicalInputIdentity.CaptureAsync(pair.Value, ct).ConfigureAwait(false);
            if (identity.Kind == PhysicalInputKind.File) result.Add(pair.Key, identity);
        }
        ct.ThrowIfCancellationRequested();
        return result.ToImmutable();
    }

    internal static async Task<ImmutableArray<PhysicalInputIdentity>> AdmitWriterOutputsAsync(string workspaceId,
        EvaluatedBuildGraph.Graph graph, EvaluatedBuildGraph.Node node, ImmutableHashSet<string> protectedInputs,
        ImmutableDictionary<string, PhysicalInputIdentity> assemblies, ImmutableArray<NativeOutputProducerObserver.Receipt> receipts,
        IWorkspacePathPolicy policy, CancellationToken ct)
    {
        await PhysicalInputIdentity.AssertCurrentAsync(assemblies.Values, ct).ConfigureAwait(false);
        var scope = new NativeWriterOutputScope(graph, node, protectedInputs, ct);
        var result = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        foreach (var receipt in receipts)
        {
            ct.ThrowIfCancellationRequested();
            if (!scope.TryGetPath(receipt, out var path) || !scope.HasVerifiedImplementation(receipt, assemblies)) continue;
            await policy.ValidateAsync(workspaceId, [path], ct).ConfigureAwait(false);
            var identity = await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
            if (identity.Kind == PhysicalInputKind.File) result.Add(identity);
        }
        var admitted = PhysicalInputIdentity.MergeConsistent(result);
        await policy.ValidateAsync(workspaceId, admitted.Select(input => input.FilePath).ToArray(), ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(assemblies.Values, ct).ConfigureAwait(false);
        await PhysicalInputIdentity.AssertCurrentAsync(admitted, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return admitted;
    }
}
