# actionlint-cache-hit-deterministic-offline-proof — Remove timing from cache-hit proof

**row:** `actionlint-cache-hit-deterministic-offline-proof` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

## Anchors

- `eng/verify-actionlint.ps1`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs`

## Acceptance

- [ ] The cache-hit regression contains no wall-clock performance threshold.
- [ ] A valid cached binary succeeds when any attempted download is forced to fail; cold acquisition and checksum verification remain covered.

## Regression

Populate the cache, rerun with the download branch configured to fail immediately, and assert successful lint, zero download attempts, and normal exit independent of host contention.
