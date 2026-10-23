# restore-output-final-callback-closure — Close retained restore output proofs after the final callback

**row:** `restore-output-final-callback-closure` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreOutputEpoch.cs`: AssertInputsAsync and CaptureAfterSuccessfulRestoreAsync.
- `src/RoslynMcp.Roslyn/Services/RestoreInputAuthority.cs`: AcceptSuccessfulAsync.
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs`: restore output capture and final execution authority.
- `tests/RoslynMcp.Tests/RestoreOutputEpochTests.cs`.

## Acceptance

- [ ] Grant the complete required known input inventory before any native read; preserve later refusal precedence over an earlier locked file.
- [ ] Renew one complete final inventory, then perform a callback-free physical comparison of every retained input before returning or publishing an epoch.
- [ ] Preserve actual caller authority, cancellation, output ownership and dependency ordering; no cached policy result or supplied no-op callback.
- [ ] Observe retained-epoch and capture-publication late callback mutations fail on old behavior; preserve actor/native-byte premises before each verdict.
- [ ] Qualify both real caller closures and unchanged native restore controls; do not claim a user effect escape from the standalone helper proof alone.

## Evidence

- causal19 native build passed zero warnings/errors in 12.31s; all 1372 C#/project hashes unchanged through compilation.
- causal19-tests.log: FinalGrantChangingEarlierInput_RefusesExistingEpoch and LastCaptureGrantChangingRetainedInput_RefusesEpochPublication expected PublicInvalidOperationException but returned without failure after actual mutation premises.
- causal19-tests.log: LaterGrantDenial_PrecedesEarlierLockedInputRead expected UnauthorizedAccessException; the old helper instead read the earlier locked file and threw IOException with native error 32 before reaching the later denial.
- Existing real caller outer proofs may detect the changed epoch; this evidence establishes the retained helper contract defect, not an unobserved producer escape.
