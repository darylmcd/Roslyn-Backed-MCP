# dotnet-11-sdk-roslyn-5-12-migration — Move the SDK floor, MSBuild 18.x and the Roslyn API family to the .NET 11 wave

**row:** `dotnet-11-sdk-roslyn-5-12-migration` · **pri:** `Medium` · **size:** `L`

## Anchors

- `global.json` — SDK floor `10.0.400`, `rollForward: latestFeature`.
- `Directory.Packages.props` — Roslyn API family `5.9.0`, `Microsoft.Build*` `17.14.28`, Extensions family `10.0.x`.
- `.github/dependabot.yml` — `ignore` blocks for `Microsoft.Build*` `>=18.0.0`, the Roslyn API family `>=5.10.0` and the Extensions runtime family `>=11.0.0`.
- `src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj:19-24` — the four `Microsoft.Build*` references with `ExcludeAssets="runtime" PrivateAssets="all"`.
- `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj:70-75` — in-repo analyzer consumed with `OutputItemType="Analyzer"`.
- `.github/workflows/ci.yml:391-422` — exact `sdk-floor (10.0.400)` lane.
- `docs/upgrade-matrix.md` — sections 1 to 3 and 7.

## Acceptance

- [ ] Operator decision recorded in an ADR under `docs/decisions/`: raise the build SDK floor to `11.0.100` (target framework stays `net10.0` or moves, stated explicitly), with a `CHANGELOG.md` migration note.
- [ ] `Microsoft.NET.StringTools` is centrally pinned and referenced with `ExcludeAssets="runtime" PrivateAssets="all"`; the four `Microsoft.Build*` pins move to one 18.x version; no `MSBL001`.
- [ ] The seven Roslyn API family pins move to one version whose compiler ships in the new SDK floor; the build has no `CS9057`.
- [ ] The Extensions runtime family decision (stay on `10.0.x` or move to `11.x`) is recorded in the same ADR and the pins match it.
- [ ] `global.json`, the `sdk-floor` lane, the CI SDK channel, `docs/upgrade-matrix.md` and `THIRD-PARTY-NOTICES.md` are updated in the same change set; the `sdk-floor` lane is green at the new floor.
- [ ] The matching `.github/dependabot.yml` `ignore` entries are removed in the same change set.
- [ ] Solution load, `build_workspace` and `test_run` are exercised against a workspace built with the .NET 11 SDK.

## Evidence

- Local SDKs (2026-11-18): `10.0.404` ships Roslyn `5.9.0` and MSBuild `18.9.15`; `11.0.100` ships Roslyn `5.12.0` and MSBuild `18.12.2`. NuGet stable Roslyn versions are `5.0.0`, `5.3.0`, `5.6.0`, `5.9.0`, `5.12.0`: one per SDK band.
- `docs/upgrade-matrix.md:21` records the same mechanism for the earlier floor: an analyzer built against a Roslyn newer than the SDK compiler fails with `CS9057`.
- `Microsoft.CodeAnalysis.Workspaces.MSBuild` `5.12.0` (net10.0 group) depends on `Microsoft.Build.Framework` `18.7.1`; `5.9.0` depends on `17.11.48`.
- PR 2126 (Roslyn API family to `5.12.0`, `Microsoft.Build.Framework` to `18.7.1`), run 43918826272: every validate leg and `sdk-floor (10.0.400)` failed with `MSBL001` for `Microsoft.NET.StringTools` `18.7.1`.
- PR 2127 (Extensions family to `11.0.0`, plus `Microsoft.CodeAnalysis.Workspaces.MSBuild` alone to `5.12.0`), run 43919015467: restore failed with `NU1107` and `NU1608` because the Roslyn API family was split.

## Context

- A server pinned to Roslyn `5.9` cannot parse language features introduced with the .NET 11 SDK compiler in user workspaces; that is the user-facing cost of holding.
- .NET 10 is the LTS line and .NET 11 is STS; that is the servicing cost of moving the Extensions family.
- Both PRs were closed on 2026-11-18 and the ignores were added so Dependabot does not re-open them weekly.
