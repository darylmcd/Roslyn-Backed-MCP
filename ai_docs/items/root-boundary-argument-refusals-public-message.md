# root-boundary-argument-refusals-public-message — Correct sanctioned-root error classification

**row:** `root-boundary-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:89-188`
- `src/RoslynMcp.Host.Stdio/Security/LegacyClientRootsNarrowingAdapter.cs:38`
- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs:58`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [x] Operator approved the configured-root/RPC category correction on 2026-10-15 for the next major release; ADR and migration delivery remain required.
- [ ] Preserve exact marked boundary-denial ArgumentException identity, InvalidArgument, opaque redaction marker and absent schemaHint. Emit no caller/configured/inner paths.
- [ ] Separate configured authority failures from request narrowing while sharing canonicalization; configured/RPC failures use safe PublicInvalidOperationException with preserved inner causes, narrowing refusals use ArgumentErrors.Redacted with actual parameter metadata. Preserve cancellation.
- [ ] Preserve existing exact-type pins for caller denials; add real production-filter wire regressions for configured, narrowing and RPC failures in both protocol eras. Do not treat an allowed/rejected fixture success string as wire-error evidence.
- [ ] Follow the approved ADR/migration and run scoped tests plus required pinned-head hosted checks after current-main rebase.

## Evidence

- Current-session diagnosis 2026-10-04: GetCanonicalRoots is used for configured authority by ClientRootPathValidator:89 and SolutionDiscoveryHelper:137/243, but for request narrowing at ClientRootPathValidator:125. Blanket argument conversion would keep server faults mislabeled as caller failures.
- The pending decision concerns released error categories under docs/release-policy.md. Plan 20261004T123100Z_backlog-remediate holds this initiative before implementation; approval was not recorded at that historical checkpoint. The decision below supersedes the policy hold only.
- Core/Services/ArgumentErrors.cs and public exception carriers are current policy sources; the retired core-move detail pointer is absent and must not be relied on.

## Context

- Original argument-error redesign survey is superseded by the current live-source diagnosis and explicit contract-care hold.
- Technical plan review passes the proposed complete correction; it does not authorize the category/release decision.

## Operator decision 2026-10-15

| Field | Record |
|---|---|
| Date | 2026-10-15 |
| Authority | Live user approval of all three recommendations in this chat; record decisions while preserving implementation and validation holds. |
| Approved policy | Approve configured-root and client-roots RPC failures changing from InvalidArgument to InvalidOperation at the next major release, with ADR and migration guidance. Preserve caller boundary denials as InvalidArgument, exact marked exception identity, path redaction and cancellation. |
| Remaining holds | Keep deferred and open. ADR/migration delivery, native replan/review, implementation, red-first tests, both protocol-era wire regressions and current CI_POLICY validation remain required. |
| Authorization boundary | Policy decision recorded only; no execution admission, release, merge or completion claim. |
| Plan supplement | ../plans/20261004T123100Z_backlog-remediate/operator-decisions-20261015.md |
