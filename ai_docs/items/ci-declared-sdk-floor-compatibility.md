# ci-declared-sdk-floor-compatibility — Exercise the declared .NET SDK floor

**row:** `ci-declared-sdk-floor-compatibility` · **pri:** `Medium` · **size:** `M`

## Anchors

- `global.json`
- `.github/workflows/ci.yml`
- `eng/verify-release.ps1`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Add a bounded lane that restores/builds with exact SDK 10.0.100, the declared floor, while retaining latest-10.0 validation.
- [ ] Exercise one representative MSBuildLocator workspace load under the floor SDK.
- [ ] Fail when CI silently exercises only a later feature band selected by `latestFeature` roll-forward.
- [ ] Document whether a future MSBuild 18.9 compile-reference upgrade raises the SDK floor and update `global.json` deliberately if required.

## Evidence

PR #1327 review found that `global.json` declares 10.0.100 with `latestFeature`, while hosted/local validation currently resolves SDK 10.0.400 and MSBuild 18.9.6. The proposed MSBuild compile-reference major therefore received no evidence against the declared 10.0.100/MSBuild 18.0 floor.
