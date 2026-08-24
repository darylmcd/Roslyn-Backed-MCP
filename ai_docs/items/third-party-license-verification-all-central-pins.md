# third-party-license-verification-all-central-pins — Verify every central package license

**row:** `third-party-license-verification-all-central-pins` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/update-third-party-notices.ps1`
- `tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs`

## Acceptance

- [ ] Resolve every central package's exact restored nuspec from the effective package-root order and validate package id, version, and normalized SPDX license expression.
- [ ] Fail closed when metadata is absent, ambiguous, malformed, or disagrees with the checked-in attribution; do not make notice verification depend on live registry access.
- [ ] Replace the ModelContextProtocol-only license special case with the shared verifier without weakening its Apache-2.0 regression.
- [ ] One regression mutates a non-MCP package nuspec license and proves deterministic verification rejects the drift.

## Evidence

The deterministic notice gate validates exact restored identity, version, and license metadata only for ModelContextProtocol. Other central-package licenses remain static declarations, so a dependency update can preserve a stale attribution while the notice gate stays green.
