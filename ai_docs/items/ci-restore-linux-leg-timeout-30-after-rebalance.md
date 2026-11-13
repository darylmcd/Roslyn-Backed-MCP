# ci-restore-linux-leg-timeout-30-after-rebalance — Restore the 30 minute cap on the four Linux shard legs

**row:** `ci-restore-linux-leg-timeout-30-after-rebalance` · **pri:** `Medium` · **size:** `S` · **verified:** `6abdb0235dad35ae04705c6dfa6a69a0c4b1f1ee` · **deps:** `ci-ubuntu-duration-profile-refresh-2604`

## Anchors

- `eng/resolve-ci-topology.ps1:86` — four `linux-N-of-4` legs carry `-TimeoutMinutes 45` and a TEMPORARY comment.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs:386` — pins the four timeouts at 45.
- `changelog.d/ci-linux-leg-timeout-45.md` — consumed at release; add a reverting fragment.

## Acceptance

- [ ] The four legs are back to 30, the pinned test expectation and the TEMPORARY comments are removed, and the first full PR run after the change keeps every Linux leg under the cap with margin.

## Evidence

- PR 2089 raised the cap to 45 on operator approval (2026-11-13) after linux-2 hit the 30 min cap with all tests passing.

## Context

- Do not execute before `ci-ubuntu-duration-profile-refresh-2604`: with the current weights linux-2 needs about 28 minutes.
