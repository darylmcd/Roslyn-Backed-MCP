using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Exact reviewed NuGet output transitions, without compiler or image authority.</summary>
internal sealed class RestoreOutputEpoch
{
    internal ImmutableArray<PhysicalInputIdentity> Inputs { get; }
    private RestoreOutputEpoch(ImmutableArray<PhysicalInputIdentity> inputs) => Inputs = inputs;

    internal static async Task<RestoreOutputEpoch> CaptureAfterSuccessfulRestoreAsync(
        IReadOnlyList<PhysicalInputIdentity> previous, IReadOnlySet<string> generatedFiles,
        IReadOnlySet<string> protectedFiles, Func<IReadOnlyList<string>, CancellationToken, Task> authorize,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var generated = generatedFiles.ToHashSet(FileSystemPath.Comparer);
        if (generated.Overlaps(protectedFiles))
            throw new PublicInvalidOperationException("A restore output overlaps a reviewed physical write; its authored bytes cannot be replaced by restore.");
        var retained = PhysicalInputIdentity.MergeConsistent(previous.Where(input => !generated.Contains(input.FilePath)));
        await authorize(retained.Select(input => input.FilePath).Concat(generated)
            .Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await AssertInputsAsync(retained, authorize, ct).ConfigureAwait(false);
        var inputs = ImmutableArray.CreateBuilder<PhysicalInputIdentity>();
        inputs.AddRange(retained);
        foreach (var path in generated.Order(FileSystemPath.Comparer))
        {
            await authorize([path], ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var input = await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
            if (input.Kind == PhysicalInputKind.Directory)
                throw new PublicInvalidOperationException("An approved NuGet output file is occupied by a directory.");
            await authorize([path], ct).ConfigureAwait(false);
            inputs.Add(input);
        }
        var epoch = new RestoreOutputEpoch(PhysicalInputIdentity.MergeConsistent(inputs));
        await epoch.AssertCurrentAsync(authorize, ct).ConfigureAwait(false);
        return epoch;
    }

    internal Task AssertCurrentAsync(Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
        => AssertInputsAsync(Inputs, authorize, ct);

    private static async Task AssertInputsAsync(IEnumerable<PhysicalInputIdentity> inputs,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var frozen = PhysicalInputIdentity.MergeConsistent(inputs);
        var paths = frozen.Select(input => input.FilePath).ToArray();
        await authorize(paths, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await PhysicalInputCaptureBatch.AssertCurrentAsync(frozen, ct).ConfigureAwait(false);
        await authorize(paths, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await PhysicalInputCaptureBatch.AssertCurrentAsync(frozen, ct).ConfigureAwait(false);
    }
}
