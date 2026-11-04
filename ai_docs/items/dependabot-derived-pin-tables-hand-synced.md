# dependabot-derived-pin-tables-hand-synced — Stop hand-syncing pins derived from Directory.Packages.props

**row:** `dependabot-derived-pin-tables-hand-synced` · **pri:** `Low` · **size:** `M`

## Anchors

- `Directory.Packages.props`
- `THIRD-PARTY-NOTICES.md`
- `docs/upgrade-matrix.md`
- `eng/update-third-party-notices.ps1`
- `tests/RoslynMcp.Tests/Workspace/ForkNativeAdmissionTests.cs:835`

## Acceptance

- [ ] A dependency-bump PR (including Dependabot) regenerates both derived tables automatically, or the verifiers derive the expected values so no hand edit is required.
- [ ] ForkNativeAdmissionTests derives the MSTest version for its generated probe csproj from Directory.Packages.props instead of hardcoding it.
- [ ] The changelog-fragment requirement for dependency bumps is handled by the same automation or documented.

## Evidence

- Dependabot PR 1944 failed ThirdPartyNoticeDriftTests (THIRD-PARTY-NOTICES.md) and the verify-ai-docs upgrade-matrix parity check; PR 1958 carried the manual fix plus a required changelog.d fragment. ForkNativeAdmissionTests.cs:835-836 pins 4.5.1 independently.
