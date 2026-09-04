# workspace-id-named-args-symbol-search — Name workspace-sensitive symbol-search arguments

**row:** `workspace-id-named-args-symbol-search` · **pri:** `Low` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`
- `tests/RoslynMcp.Tests/SymbolSearchPaginationTests.cs`
- `tests/RoslynMcp.Tests/Top10V3RegressionTests.cs`

## Acceptance

- [ ] Convert every affected direct read-only tool invocation in the three files to fully named arguments.
- [ ] Preserve argument expressions, evaluation order, explicit optional values, and assertions.
- [ ] Make no production-signature or runtime-behavior change; require compile check and all three targeted test classes to pass.

## Evidence

The live census found positional calls whose required arguments follow `workspaceId`; moving that parameter for a later optional default would otherwise change binding or fail compilation.

## Context

Split from `workspace-id-optional-named-argument-prerequisite` after its two-file estimate expanded to 16 files.
