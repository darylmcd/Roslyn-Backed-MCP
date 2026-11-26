# change-signature-preview-latency — Bring change_signature_preview under 30 s on RoslynMcp.slnx

**row:** `change-signature-preview-latency` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:286-318`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:1241-1305`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:401-435`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs:108-123`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs:214-217`
- `tests/RoslynMcp.Tests/ChangeSignatureBindingsTests.cs`

## Acceptance

- [ ] A failed `change_signature_preview` returns well inside the writer budget: on `RoslynMcp.slnx` an op=add with an unknown `parameterType` is refused in under 30 s (56.3 s measured 2026-11-21 at f7016d6c after PR1; the audit saw 14-48 s).
- [ ] A successful add/remove/reorder on `PhysicalPathResolver.CanonicalizeDriveRoot` returns in under 30 s (67.2 s measured).
- [ ] Every refusal and diagnostic-evidence check covered by the existing `ChangeSignature*` suites still fires (no weaker verification); before/after seconds recorded in the row note.

## Evidence

- Measured with a throwaway `ChangeSignatureTestBase` harness + Stopwatch on the slnx (cold): refusal (CS0246 at `ValidateCandidateAsync`) 56.3 s; success 67.2 s. Baseline `GetDiagnostics` (`:292-300`), the scan binding and the candidate diagnostics (`:1186`) are three full bindings.
- Split from `change-signature-unrelated-invocation-rewrite` bullet 4.

## Context

The baseline diagnostics pass runs before any scan, so it cannot move without restructuring `BuildAsync`, which `change-signature-builder-decompose` and `change-signature-preview-builder-build-async-decomposition` also rewrite: sequence with them. Cut passes without dropping a check.

## Notes

2026-11-25 policy/source re-derivation supersedes the earlier mandatory-decomposition blocker: current remediation permits one complete shared-mechanism cluster; size/operation count alone does not force a split. No operator decision is needed merely for valid disposable fixture preparation. The old56.3/67.2s figures are historical at f7016d6c, not fresh timings or proof of three independent full bindings. Future measurement must establish that premise without dropping diagnostics, binding, caller-info, cancellation or publication checks. Valid setup: original method/callers for unknown-type refusal and adding an unused required int marker; for remove/reorder, prepare an independent copied solution with (string pathRoot,int latencyMarker), NO default, and append literal0 to both semantically resolved calls. Remove index1 or reorder1,0; preserve original pathRoot/body/argument bindings and compiler validity. Optional marker before required pathRoot is invalid CS1737 and is not a successful reorder witness. Exclude setup/workspace load from invocation-through-response timing, use a fresh process/workspace per case, record actual prewarm conditions, and keep <30s plus all existing checks. No current runtime/semver proof or new cache/mechanism is authorized; D5/D6/D8/V3 decisions still apply if a later design actually needs them.
