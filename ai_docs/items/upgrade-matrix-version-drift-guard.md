# upgrade-matrix-version-drift-guard — Keep documented package pins synchronized

**row:** `upgrade-matrix-version-drift-guard` · **pri:** `Low` · **size:** `M`

## Anchors

- `docs/upgrade-matrix.md`
- `Directory.Packages.props`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] The ModelContextProtocol and Microsoft.NET.Test.Sdk versions in `docs/upgrade-matrix.md` match `Directory.Packages.props`.
- [ ] The AI-doc validation gate fails with an actionable message when a package pin changes without the corresponding matrix row.

## Evidence

- The 2026-08-12 open-PR audit found the matrix still says ModelContextProtocol 1.1.0 and Microsoft.NET.Test.Sdk 17.14.0 while the central pins are 1.4.1 and 17.14.1.

## Context

Implement a narrow version-parity check for matrix rows that claim an exact current pin. Do not turn the prose matrix into a second package manifest.
