# release-5-0-0-cut — Cut 5.0.0 once every row parked on the next major has shipped

**row:** `release-5-0-0-cut` · **pri:** `Medium` · **size:** `L` · **deps:** `release-4-4-0-locationdto-deprecation-cut,root-boundary-argument-refusals-public-message,tool-error-handler-argument-sniffing-arms-retired,argument-exception-public-message-guard,server-info-update-available-next-major-nullable,workspace-not-found-next-major-category-promotion,project-name-not-found-category-next-major,locationdto-next-major-flat-field-removal,tool-alias-deprecation-stale-removal-major,v5-major-release-contract-prereqs,locationdto-partial-span-representation-5-0`

## Anchors

- `.claude/skills/release-cut/SKILL.md`
- `docs/decisions/README.md`
- `docs/product-contract.md`
- `docs/release-policy.md`
- `CHANGELOG.md`

## Acceptance

- [ ] `rg -n -i "next major|next-major|5\.0\.0|BLOCKED until" ai_docs/backlog.md` returns no open row other than this one.
- [ ] ADR 0011 indexes in `docs/decisions/README.md` and records old to new wire behavior plus consumer migration for every 5.0 breaking change; `docs/product-contract.md` § Deprecations scheduled for 5.0 is replaced by the 5.0 contract.
- [ ] `changelog.d/` has no fragment lacking a migration note when its category is `Changed — BREAKING`.
- [ ] `/release-cut major` completes: preflight, `eng/verify-release-environment.ps1` exit 0, verify (`-Configuration Release -NoCoverage -RequireConsumedFragments`) exit 0, release PR merged, `v5.0.0` tag on origin, `publish-nuget` green, `eng/verify-install-layers.ps1` exit 0, pinned `dnx` canary reports 5.0.0.
- [ ] DevBuild-Runner parity job run at the full 40-character SHA of clean merged `main`; both result lines reported (AGENTS.md pilot).

## Evidence

- 144 unreleased fragments, 9 of them `Changed — BREAKING`, force a major from `main`.
- The six parked rows are listed in the deps cell; the guard chain is root-boundary, then sniffing-arms, then the guard.

## Context

Plan: `release-plan-4.4.0-then-5.0.0` Phases B and C. Close backlog rows in the PRs that ship them, not in the release PR.
