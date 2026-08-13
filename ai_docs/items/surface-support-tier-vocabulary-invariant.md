# surface-support-tier-vocabulary-invariant — Reject unknown catalog tiers at startup

**row:** `surface-support-tier-vocabulary-invariant` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/SurfaceRegistrationPolicy.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ToolTierSelection.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`

## Acceptance

- [ ] Fail startup with the entry kind, name, and tier unless every tool, resource, and prompt uses exactly `stable` or `experimental`.
- [ ] Keep selection parsing and catalog validation on one shared tier vocabulary.
- [ ] Add one regression per surface kind proving a metadata/catalog tier typo cannot be silently filtered while parity still passes.

## Evidence

- Registration calls `selection.Includes(entry.SupportTier)` and expected counts use the same predicate; a shared typo is silently dropped and can still report parity success.
