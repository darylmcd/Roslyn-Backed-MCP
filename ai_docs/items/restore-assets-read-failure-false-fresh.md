# restore-assets-read-failure-false-fresh — Distinguish failed assets reads from fresh restore state

**row:** `restore-assets-read-failure-false-fresh` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:224-268`
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`

## Acceptance

- [ ] Assets read/parse failure cannot be reported as verified fresh state; distinguish unevaluable project metadata from known package expectations with unusable assets through the existing restore decision and diagnostics contract.
- [ ] Observe red-first malformed JSON and unreadable-assets regressions with known PackageReference expectations; retain genuine no-package/fresh controls, cancellation, alternate assets layouts and safe diagnostics. Re-derive all callers before selecting failure/refusal/restore behavior.

## Evidence

| Snapshot | Source evidence | Verification limit |
|---|---|---|
| Main337fc2cde10eca3f591cc016c146dee7b893e26a | IsRestoreRequired reads expected packages and LoadAssetsPackageVersions; catch263-266 logs Debug for IO/Unauthorized/Json/InvalidOperation, then returns false268. Malformed JSON or unreadable assets therefore share the fresh result. | Source trace only; dedicated causal runtime reproduction owed. |

## Context

- Distinct exception-to-fresh decision mechanism from restore-callers-missing-packages-path's cache-root planning and valid JSON ValueKind guards; no runtime fix claimed here.
- Found while checking restore executor WIP; filing outside the existing selected initiative set. Preserve scope and routing of the active plan.
