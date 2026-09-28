# root-boundary-argument-refusals-public-message — Sanctioned-root boundary refusals are expressed via ArgumentErrors

**row:** `root-boundary-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`
- `src/RoslynMcp.Host.Stdio/Security/LegacyClientRootsNarrowingAdapter.cs`
- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] ClientRootPathValidatorTests stay green without changing their ThrowsExactly<ArgumentException> pins.
- [ ] No construction of ArgumentException remains in the 3 files.
- [ ] ClientRootPathValidatorTests.cs is shared with core-move (using-only edit, ordered by dependsOn).

## Evidence

- 3 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
