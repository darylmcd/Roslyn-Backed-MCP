# type-extraction-service-dead-fallback-and-stale-naming — consolidated low-severity cleanup in TypeExtractionService

**row:** `type-extraction-service-dead-fallback-and-stale-naming` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:139` (`PartitionMembers` — the filter that makes the line-404 fallback unreachable)
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:382` (`CollectExtractTypeDanglingReferenceWarnings` / `warnings` locals — stale advisory naming)
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:404` (`GetMemberName(member) ?? member.Kind().ToString()` — unreachable fallback)

## Acceptance

- [ ] Line 404's fallback is either removed (with a comment pointing at the `PartitionMembers` filter that makes it unreachable) or kept with a corrected comment stating it is defensive-only.
- [ ] `CollectExtractTypeDanglingReferenceWarnings` and its `warnings` locals are renamed to `CollectExtractTypeBlockingDependencies` / `blockingDependencies` to match the shipped fatal-refusal contract (values are surfaced to callers as `blockingDependencies`, not advisory warnings).
- [ ] Existing `TypeExtractionTests` pass unchanged.

## Evidence

- Code-quality review of PR #1138 (`extract-type-preview-refusal-missing-blocking-deps`), two low-severity findings in the same file consolidated per the sweep's filing gate (no standalone rows for low cosmetic items): (1) `PartitionMembers` (line 139) only admits a member into `toExtract` when `GetMemberName(member)` is non-null, so `membersToExtract` never contains an unnamed shape — the line-404 fallback cannot fire. (2) The helper/locals still read as advisory "warnings" although the values are fatal refusal causes surfaced as `blockingDependencies`.

## Context

Spin-off from the `extract-type-preview-refusal-missing-blocking-deps` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1138). Both findings are naming/dead-path hygiene, not functional bugs.
2026-08-13 adjacent review: also delete the orphan duplicate XML summary near the composition-injection helper while consolidating stale warnings/fallback naming; keep this within the existing one-production-file cleanup.
