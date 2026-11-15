# ci-restore-linux-leg-timeout-30-after-rebalance — Restore the 30 minute cap on hosted Linux shard legs

**row:** `ci-restore-linux-leg-timeout-30-after-rebalance` · **pri:** `Medium` · **size:** `S` · **deps:** `ci-ubuntu-duration-profile-refresh-2604`

## Anchors

- `eng/resolve-ci-topology.ps1:84-95` — four code and four docs-only Linux shards use 45-minute caps. The code topology has a temporary-cap comment.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:385-405` — both groups pin all Linux shard caps to 45.
- `CHANGELOG.md:71-72` — v5.1.0 published the temporary timeouts. Add a reverting fragment when the caps change.

## Acceptance

- [ ] Restore 30-minute caps for all four code and all four docs-only Linux shard legs. Update the pinned contract expectations to 30 and remove the temporary-cap comments.
- [ ] Demonstrate that the first full code and docs-only PR runs after the change keep every Linux leg under the cap with margin.

## Evidence

- PR 2089 raised the code cap to 45 on operator approval (2026-11-13) after linux-2 hit 30 minutes with all tests passing. PR 2090 aligned the docs-only cap.
- Release v5.1.0 consumed both original fragments.

## Context

- Do not execute before `ci-ubuntu-duration-profile-refresh-2604`. The existing weights put linux-2 near 28 minutes of tests.
