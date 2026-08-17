# third-party-notice-generation-drift — Generate verified dependency notices

**row:** `third-party-notice-generation-drift` · **pri:** `Medium` · **size:** `M`

## Anchors

- `THIRD-PARTY-NOTICES.md`
- `Directory.Packages.props`
- New `eng/update-third-party-notices.ps1`.
- New `tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs`.

## Acceptance

- [ ] Generate the notice package/version inventory from the centrally managed runtime/build/test package graph instead of a hand-maintained claim.
- [ ] Preserve reviewed license and project attribution through a deterministic mapping that fails on an unknown package.
- [ ] Add a verify-only mode to the release/docs gate so pin drift is detected before merge.
- [ ] One fixture proves the current MCP/Roslyn versions match `Directory.Packages.props` and an intentional mismatch fails.

## Evidence

- The notice claims it is generated from central package management but lists `ModelContextProtocol` 1.1.0 and Roslyn 5.3.0 while the checkout pins 2.1.0 and 5.6.0; no generator or verifier exists. The file is not currently packed with the host artifact, so this is repository/legal-document correctness rather than a shipped-package blocker.
