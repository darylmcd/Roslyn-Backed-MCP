# ci-restore-linux-leg-timeout-30-after-rebalance — Restore the 30 minute cap on the four Linux shard legs

**row:** `ci-restore-linux-leg-timeout-30-after-rebalance` · **pri:** `Medium` · **size:** `S` · **deps:** `ci-ubuntu-duration-profile-refresh-2604`

## Anchors

- `eng/resolve-ci-topology.ps1:86` — four `linux-N-of-4` code legs and four `docs-linux-N-of-4` docs legs carry `-TimeoutMinutes 45` (PRs 2089 and 2090); the code legs also carry a TEMPORARY comment.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:386` — pins all eight timeouts at 45.
- `changelog.d/ci-linux-leg-timeout-45.md`, `changelog.d/ci-docs-linux-leg-timeout-45.md` — consumed at release; add a reverting fragment.

## Acceptance

- [ ] The four legs are back to 30, the pinned test expectation and the TEMPORARY comments are removed, and the first full PR run after the change keeps every Linux leg under the cap with margin.

## Evidence

- PR 2089 raised the cap to 45 on operator approval (2026-11-13) after linux-2 hit the 30 min cap with all tests passing.

## Context

- Do not execute before `ci-ubuntu-duration-profile-refresh-2604`: with the current weights linux-2 needs about 28 minutes.
