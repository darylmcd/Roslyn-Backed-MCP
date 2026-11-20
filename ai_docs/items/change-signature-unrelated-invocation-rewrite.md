# change-signature-unrelated-invocation-rewrite — Limit change_signature add/remove/reorder to the target call sites

**row:** `change-signature-unrelated-invocation-rewrite` · **pri:** `High` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:53`
- `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Red first on SampleSolution: `change_signature_preview(op=add, metadataName=SampleLib.AnimalService.CountAnimals, name=strict, parameterType=bool, defaultValue=false)` must leave `Assert.AreEqual(2, count);` in `SampleLib.Tests/AnimalServiceTests.cs` untouched (today it becomes an explicit named-argument generic call).
- [ ] Red first on this solution: `op=add` on `RoslynMcp.Roslyn.Helpers.PhysicalPathResolver.CanonicalizeDriveRoot` returns a preview instead of "An existing argument has no original semantic parameter binding."
- [ ] Re-derive which invocations the builder visits; only invocations bound to the target symbol are rewritten or validated. Cover add, remove and reorder.
- [ ] A failed preview returns well inside the writer budget (failures took 14-48 s).

## Evidence

- P6-F8: 11/11 add/remove/reorder calls failed on RoslynMcp.slnx across five methods, including on a fresh server process.
- P18-F1: op=add on SampleSolution rewrote an unrelated MSTest assertion (caller-argument-expression parameters materialized).
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).

## Context

- Both symptoms point at the same mechanism (argument binding walked for invocations that are not call sites of the target); that link is inferred from the throw site, not proven.
