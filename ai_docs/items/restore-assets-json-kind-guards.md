# restore-assets-json-kind-guards — Guard malformed JSON kinds across assets readers

**row:** `restore-assets-json-kind-guards` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:37` — Object access and libraries enumeration lack kind checks.
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:529` — Nested framework/dependency/central-version accesses require compatible JSON kinds.
- `tests/RoslynMcp.Tests/ProjectMetadataParserTryGetResolvedPackageVersionTests.cs`
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`

## Acceptance

- [ ] Valid JSON with incompatible kinds never escapes an assets reader as InvalidOperationException; unknown resolved versions remain unknown and unusable assets with known package expectations require restore with one sanitized diagnostic.
- [ ] Probe every object/string/enumeration access in these readers for the same kind-check mechanism; retain malformed-syntax, redaction, missing/default assets and cancellation contracts.
- [ ] Observe genuine old-behavior kind failures, preserve existing assertions, obtain independent review, and pass scoped/hosted checks plus the serialized delivery gate before closure.

## Scope

- One regression mechanism across two readers; two production files, two test files and one fragment.
- Assets path selection belongs to dependent restore-active-assets-layout-authority.

## Evidence

- Fresh original-production run restore-complete-callers-json-red.log reproduced six parser kind exceptions among 14 failures/61 passes.
- The guarded detector checkpoint exposed two existing diagnostic assertions: restore-complete-assets-green.log recorded 2 failures/48 passes because an early unknown-assets return omitted logging. Fix the diagnostic path; do not weaken assertions.
- Read-only references: ea969292, origin/codex/restore-reviewed-graph and stash codex-remediate-20261022-preserve-restore. Never pop/drop the stash.
