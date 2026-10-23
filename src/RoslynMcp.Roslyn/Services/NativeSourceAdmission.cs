using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>One operation's frozen source evidence, checked before native preparation can advance outputs.</summary>
internal sealed class NativeSourceAdmission
{
    private readonly PhysicalMutationRecovery _operation;
    private readonly NativeBuildMembership _membership;
    private readonly PhysicalInputClosure _captured;
    private NativeCompilerContextCertificate.Epoch.CertifiedEpoch? _initial;

    private NativeSourceAdmission(PhysicalMutationRecovery operation)
    {
        _operation = operation;
        _membership = (operation.CapturedLayout
            ?? throw InvalidOperationErrors.Internal("A source admission requires its captured layout.")).RequireCapturedSourceMembership();
        _captured = PhysicalInputClosure.FromCaptured(operation);
    }

    // The manager checks exact active operation ownership before issuing this value.
    internal static NativeSourceAdmission ForOperation(PhysicalMutationRecovery operation) => new(operation);

    internal async Task AssertInitialAsync(string workspaceId, NativeCompilerContextCertificate.Epoch epoch,
        IWorkspacePathPolicy policy, CancellationToken ct)
    {
        if (workspaceId != _operation.WorkspaceId || _initial is not null
            || epoch is not NativeCompilerContextCertificate.Epoch.CertifiedEpoch initial)
            throw new WorkspaceMutationOwnershipException("The source admission has no unique certified initial native epoch.");
        if (!_membership.EquivalentTo(initial.Membership))
            throw new PublicInvalidOperationException("The native source graph changed before physical mutation preparation.");
        var closure = _captured.Merge(PhysicalInputClosure.FromEpoch(initial));
        await policy.ValidateAsync(workspaceId, closure.CurrentGrants, ct).ConfigureAwait(false);
        _membership.AssertCurrent(ct);
        initial.Membership.AssertCurrent(ct);
        await closure.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
        _initial = initial;
    }

    internal async Task<SourceAdmissionReceipt> IssueAsync(string workspaceId,
        NativeCompilerContextCertificate.Epoch finalEpoch, NativeCompilerContextCertificate context,
        IWorkspacePathPolicy policy, CancellationToken ct)
    {
        if (workspaceId != _operation.WorkspaceId || _initial is null
            || finalEpoch is not NativeCompilerContextCertificate.Epoch.CertifiedEpoch final
            || context.Availability != NativeCompilerContextAvailability.Certified)
            throw new WorkspaceMutationOwnershipException("Native source preparation did not preserve its certified admission chain.");
        // Initial overlapping slots advance only through the loader's actual producer/
        // reconciliation chain. Extra captured inputs never gain a new baseline.
        var initialPaths = _initial.Inputs.Select(input => input.FilePath).ToHashSet(FileSystemPath.Comparer);
        var retained = _captured.Except(initialPaths).Merge(PhysicalInputClosure.FromEpoch(final));
        await policy.ValidateAsync(workspaceId, retained.CurrentGrants, ct).ConfigureAwait(false);
        final.Membership.AssertCurrent(ct);
        await retained.AssertPhysicalCurrentAsync(ct).ConfigureAwait(false);
        return SourceAdmissionReceipt.Issue(this, context, retained);
    }

    internal sealed class SourceAdmissionReceipt
    {
        private readonly PhysicalMutationRecovery _operation;
        private readonly NativeCompilerContextCertificate _context;
        private readonly PhysicalInputClosure _inputs;
        private SourceAdmissionReceipt(PhysicalMutationRecovery operation,
            NativeCompilerContextCertificate context, PhysicalInputClosure inputs)
        {
            _operation = operation;
            _context = context;
            _inputs = inputs;
        }

        internal static SourceAdmissionReceipt Issue(NativeSourceAdmission admission,
            NativeCompilerContextCertificate context, PhysicalInputClosure inputs)
        {
            if (admission._initial is null || context.Availability != NativeCompilerContextAvailability.Certified)
                throw new WorkspaceMutationOwnershipException("The native source receipt has no completed initial admission.");
            return new(admission._operation, context, inputs);
        }

        internal Task AssertCurrentAsync(PhysicalMutationRecovery operation, Solution solution,
            NativeCompilerContextCertificate context, IWorkspacePathPolicy policy, IReadOnlyList<string> sourceCoordinates, CancellationToken ct)
        {
            if (!ReferenceEquals(operation, _operation) || !ReferenceEquals(context, _context))
                throw new WorkspaceMutationOwnershipException("The native source receipt belongs to another operation or candidate.");
            return context.AssertLoadCurrentAsync(solution, operation.WorkspaceId, policy,
                SupplementalPackageReadAuthority.None, _inputs.WithCurrentGrants(sourceCoordinates), ct);
        }
    }
}
