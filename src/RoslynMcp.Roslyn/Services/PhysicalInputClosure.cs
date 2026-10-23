using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Frozen physical inputs and exact current grants, without a semantic graph claim.</summary>
internal sealed class PhysicalInputClosure
{
    private PhysicalInputClosure(IEnumerable<PhysicalInputIdentity> inputs, IEnumerable<string> grants,
        ImmutableArray<SdkExistenceInput> sdkInputs)
    {
        Inputs = PhysicalInputIdentity.MergeConsistent(inputs);
        CurrentGrants = grants.Distinct(FileSystemPath.Comparer).ToImmutableArray();
        SdkInputs = sdkInputs;
    }

    internal ImmutableArray<PhysicalInputIdentity> Inputs { get; }
    internal ImmutableArray<string> CurrentGrants { get; }
    private ImmutableArray<SdkExistenceInput> SdkInputs { get; }

    internal static PhysicalInputClosure FromExpectation(PhysicalGraphExpectation expected)
        => new(expected.BuildInputs.Concat(expected.Packages.Inputs),
            expected.Packages.PathsRequiringCurrentGrant(expected.BuildInputs.Select(input => input.FilePath))
                .Concat(expected.Packages.RequiresCurrentGrant), expected.SdkInputs);

    internal static PhysicalInputClosure FromCaptured(PhysicalMutationRecovery operation)
    {
        var layout = operation.CapturedLayout
            ?? throw InvalidOperationErrors.Internal("A source admission has no captured layout.");
        var inputs = PhysicalInputIdentity.MergeConsistent(operation.CapturedGraphFiles
            .Concat(layout.ConfigurationInputs).Concat(layout.ConditionInputs).Concat(layout.Packages.Inputs));
        return new(inputs, layout.Packages.PathsRequiringCurrentGrant(inputs.Select(input => input.FilePath)
            .Where(path => !layout.IsImmutablePath(path))).Concat(layout.Packages.RequiresCurrentGrant), layout.SdkInputs);
    }

    internal static PhysicalInputClosure FromEpoch(NativeCompilerContextCertificate.Epoch.CertifiedEpoch epoch)
        => new(epoch.Inputs, epoch.Mutable, epoch.SdkInputs);

    internal PhysicalInputClosure Except(IReadOnlySet<string> paths)
        => new(Inputs.Where(input => !paths.Contains(input.FilePath)), CurrentGrants.Where(path => !paths.Contains(path)), SdkInputs);

    internal PhysicalInputClosure Merge(PhysicalInputClosure other)
        => new(Inputs.Concat(other.Inputs), CurrentGrants.Concat(other.CurrentGrants), SdkInputs.AddRange(other.SdkInputs));

    internal PhysicalInputClosure WithCurrentGrants(IReadOnlyList<string> paths)
        => new(Inputs, CurrentGrants.Concat(paths), SdkInputs);

    internal async Task AssertPhysicalCurrentAsync(CancellationToken ct)
    {
        SdkExistenceInput.AssertCurrent(SdkInputs, ct);
        await PhysicalInputCaptureBatch.AssertCurrentAsync(Inputs, ct).ConfigureAwait(false);
        SdkExistenceInput.AssertCurrent(SdkInputs, ct);
    }
}
