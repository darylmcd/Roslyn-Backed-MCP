# cryptography-pin-rationale-deduplication — Remove stale duplicated security-pin prose

**row:** `cryptography-pin-rationale-deduplication` · **pri:** `Low` · **size:** `M`

## Anchors

- `Directory.Packages.props`
- `src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj`
- `tests/RoslynMcp.Tests/PackageFamilyContractTests.cs`

## Acceptance

- [ ] Keep advisory history and the current `System.Security.Cryptography.Xml` pin rationale in one central location.
- [ ] Remove version-specific duplicate prose from the consuming project or replace it with a pointer to the central pin.
- [ ] Update the rationale whenever the pin moves; do not retain “as of this pin” text naming an older version.
- [ ] Add one regression that rejects conflicting numeric versions for the same security pin across central metadata and project comments.

## Evidence

PR #1326 would move `System.Security.Cryptography.Xml` to 10.0.11 while `Directory.Packages.props` still described 10.0.10 as current and `RoslynMcp.Roslyn.csproj` retained separate prose naming 10.0.6. The duplicated comments are already stale independently of the proposed upgrade.
