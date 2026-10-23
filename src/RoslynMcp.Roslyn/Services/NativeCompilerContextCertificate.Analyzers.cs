using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal sealed partial class NativeCompilerContextCertificate
{
    private AnalyzerInputCatalog? _analyzerInputs;
    private AnalyzerReferenceIsolation.BindingPreflight? _analyzerBinding;

    internal async Task<NativeCompilerContextCertificate>
        CaptureAnalyzerInputsAsync(string workspaceId, IWorkspacePathPolicy policy, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || Availability != NativeCompilerContextAvailability.Certified)
            return this;
        if (_analyzerInputs is not null) throw InvalidOperationErrors.Internal("Analyzer isolation was already prepared.");
        var authority = RequireCertified();
        var reader = BindInputAuthority((paths, token) => policy.ValidateAsync(workspaceId, paths, token), ReadPhysicalAsync, ReadPhysicalDigestAsync);
        AnalyzerInputCatalog catalog;
        try
        {
            await AssertLoadEpochAsync(workspaceId, policy, [], ct).ConfigureAwait(false);
            catalog = await AnalyzerInputCatalog.CaptureAsync(NativeSolution, authority.Packages.Files,
                reader.Authorize, reader.Read, reader.Digest, ct).ConfigureAwait(false);
            await AssertLoadEpochAsync(workspaceId, policy, [], ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (WorkspacePathPolicyRefusal.IsDenied(failure))
        {
            // No loader was installed and no shadow file was created. Keep the trusted
            // native semantic snapshot, with no claim that our dependency reads are approved.
            return new NativeCompilerContextCertificate(NativeSolution, RefusalAvailability(failure), CaptureLoadEvidence());
        }
        _analyzerInputs = catalog;
        _analyzerBinding = AnalyzerReferenceIsolation.CaptureBindingPreflight(NativeSolution, catalog, ct);
        await AssertLoadEpochAsync(workspaceId, policy, [], ct).ConfigureAwait(false);
        if (_analyzerBinding.Status != AnalyzerReferenceIsolation.BindingStatus.Bound)
            return new NativeCompilerContextCertificate(NativeSolution, NativeCompilerContextAvailability.AnalyzerImageBindingUnavailable,
                CaptureLoadEvidence()) { _analyzerInputs = catalog };
        return this;
    }

    internal async Task<AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease> InstallAnalyzerIsolationAsync(
        string workspaceId, IWorkspacePathPolicy policy, ILogger logger, CancellationToken ct)
    {
        await _normalizationGate.WaitAsync(ct).ConfigureAwait(false);
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease = null;
        try
        {
            if (_issuedProjection is not null || _preparedProjection is not { } prepared)
                throw InvalidOperationErrors.Internal("Native publication requires its one prepared compiler projection.");
            var projection = prepared.Projection;
            AnalyzerReferenceIsolation.RetargetResult? installed = null;
            if (Availability == NativeCompilerContextAvailability.Certified)
            {
                if (OperatingSystem.IsWindows())
                {
                    if (_analyzerInputs is not { } catalog || _analyzerBinding is not { } binding)
                        throw InvalidOperationErrors.Internal("Analyzer publication requires the frozen catalog and native binding preflight.");
                    await AssertProjectionInputsCurrentAsync(prepared.Parsing, workspaceId, policy, ct).ConfigureAwait(false);
                    installed = await AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoaderAsync(
                        NativeSolution, projection.Solution, workspaceId, catalog, binding, logger, ct).ConfigureAwait(false);
                    lease = installed.Lease;
                    if (installed.Status != AnalyzerReferenceIsolation.BindingStatus.Bound)
                        throw new PublicInvalidOperationException("Analyzer image binding changed before native workspace publication; reload before executing generators.");
                    projection = projection with { Solution = installed.Solution };
                }
                await AssertProjectionInputsCurrentAsync(prepared.Parsing, workspaceId, policy, ct).ConfigureAwait(false);
                installed?.AssertCurrent(ct);
            }
            else
            {
                await AssertLoadClosureAsync(prepared.Parsing, workspaceId, policy,
                    CaptureLoadEvidence().SupplementalPackages, [], null, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            BindNormalized(projection.Solution, prepared.Produced, prepared.Parsing);
            _issuedProjection = projection;
            _preparedProjection = null;
            return lease ?? AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.CreateEmpty();
        }
        catch (Exception failure)
        {
            if (lease is not null)
                OwnedResourceCleanup.Dispose("Analyzer projection publication cleanup failed.", failure, lease);
            throw;
        }
        finally { _normalizationGate.Release(); }
    }
}
