# find-implementations-corlib-root-tighten-heuristic — tighten IsCorlibAssembly beyond StartsWith("System.")

**row:** `find-implementations-corlib-root-tighten-heuristic` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (`IsCorlibAssembly` / `IsCorlibImplementationRoot`)

## Acceptance

- [ ] Tightened to a `SpecialType`/well-known-type check or an explicit corlib-assembly allowlist, keeping `StartsWith` only as a documented fallback
- [ ] Regression: guard asserts a `System.*`-named non-corlib metadata interface is NOT classified as a corlib implementation root

## Evidence

- 2026-06-05 top-5 remediation code-quality review (find-implementations-corlib row).

## Context

Follow-on to `find-implementations-corlib-metadataname-zero` (SHIPPED, PR pending). `ReferenceService.IsCorlibAssembly` classifies a corlib/BCL assembly via a broad `name.StartsWith("System.", Ordinal)` match, which over-matches the full `System.*` surface (and any third-party assembly literally named `System.*`) versus the tight `SpecialType`-based gate the sibling #754 guard (`IsCorlibVirtualMember`) uses. NOT a correctness regression — the metadata-only (`!IsInSource`) gate already prevents the hint from hiding reliably-enumerable source-declared interfaces, and the hint is caller-recoverable — but it is imprecise.
