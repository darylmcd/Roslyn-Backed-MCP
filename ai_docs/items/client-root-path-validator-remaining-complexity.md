# client-root-path-validator-remaining-complexity — Reduce remaining path-validator hotspots

**row:** `client-root-path-validator-remaining-complexity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:43-104`
- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:168-217`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] `ValidatePathAgainstRootsAsync` and `ResolvePath` each measure cyclomatic complexity at or below 8.
- [ ] Symlink/junction, nonexistent descendant, sanctioned-root, and cancellation behavior remain unchanged.
- [ ] New or changed helpers remain at or below CC 8 and add no extra filesystem probes.

## Evidence

- Read-side Roslyn metrics on 2026-07-17 measured `ValidatePathAgainstRootsAsync` at CC 10 and `ResolvePath` at CC 9.

## Dependencies

- `client-root-path-validator-complexity-extraction`
