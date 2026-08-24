# dependabot-msbuild-family-alignment — Keep compile-time MSBuild packages coherent

**row:** `dependabot-msbuild-family-alignment` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/dependabot.yml`
- `Directory.Packages.props`
- `eng/verify-release.ps1`
- `tests/RoslynMcp.Tests/PackageFamilyContractTests.cs`

## Acceptance

- [ ] Configure Dependabot to update `Microsoft.Build`, `Microsoft.Build.Framework`, `Microsoft.Build.Tasks.Core`, and `Microsoft.Build.Utilities.Core` as one family, including major updates, before the generic NuGet group.
- [ ] Fail release verification when the four central pins differ.
- [ ] For an 18.x upgrade, promote any MSBuild runtime dependency such as `Microsoft.NET.StringTools` to the required direct `PrivateAssets=all` and `ExcludeAssets=runtime` reference.
- [ ] Add one regression that supplies a split family and proves the gate rejects it before restore/build artifacts can be published.

## Evidence

Dependabot PR #1327 raised only `Microsoft.Build.Framework` from 17.14.28 to 18.9.6. Exact merged-tree restore resolved the remaining MSBuild family at 17.14.28 and `Microsoft.NET.StringTools` at 18.9.6; Release build then failed MSBL001 because the transitive runtime package lacked the required direct exclusion metadata.
