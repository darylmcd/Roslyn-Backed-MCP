# compile-check-scope-vocab-and-untested-branches — consolidated low-severity cleanup in compile_check's scope vocabulary

**row:** `compile-check-scope-vocab-and-untested-branches` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:112` (`ComputeRequestedScope` project/solution branches — untested)
- `src/RoslynMcp.Core/Models/CompileCheckDto.cs:18` (scope vocabulary restated in 4 unlinked places)
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:99-102` (private consts duplicating the vocabulary)

## Acceptance

- [ ] Add cheap assertions covering `ComputeRequestedScope`'s untested branches: `projectName`-only → `requested==actual=="project"`; no filters → both `"solution"`; `projectName`+`files` → `"project"` (documented precedence).
- [ ] The `"files"`/`"project"`/`"solution"` vocabulary is hoisted to public consts (or a JSON-string-backed enum) on the Core model and referenced from the service, tool description, and tests instead of being restated in 4 places.

## Evidence

- Code-quality review of PR #1151 (`compile-check-multi-project-fallback-structured-scope`): two low-severity findings — `ComputeRequestedScope`'s project/solution branches (including the documented projectName-wins-over-files precedence) have no test; and the scope-value vocabulary is duplicated across the DTO doc, service consts, tool description, and test literals with no shared source.

## Context

Spin-off from the `compile-check-multi-project-fallback-structured-scope` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1151). Both findings are test-coverage/duplication hygiene, not functional bugs; consolidated per the sweep's filing gate.
