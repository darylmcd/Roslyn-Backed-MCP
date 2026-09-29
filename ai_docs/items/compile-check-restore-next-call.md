# compile-check-restore-next-call — Provide a structured reload action

**row:** `compile-check-restore-next-call` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/CompileCheckDto.cs`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:57-66`
- `src/RoslynMcp.Host.Stdio/Tools/CompileCheckTools.cs:25`
- (new) `src/RoslynMcp.Core/Models/NextCallDto.cs`
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs:148-156`
- (new) `tests/RoslynMcp.Tests/CompileCheckRestoreRequiredWireTests.cs`

## Acceptance

- [ ] Preserve the existing `success:false`, zero-count, `readiness:"restore-required"` result and add top-level `nextCall` naming `workspace_reload` with the same `workspaceId` and `autoRestore:true`.
- [ ] Set `nextCall` in `CompileCheckService` so embedded compile results carry it; update the tool remarks.
- [ ] A red-first wire test exercises the real tool result, and the service test asserts the action.
- [ ] Keep `compile_check` read-only; it must not restore or reload within its read lock.

## Evidence

- Parent `compile-check-restore-required-handshake`: `CompileCheckService.cs:57-66` returns the restore-required result without a machine-actionable call. The result shape shipped in v4.2.0.

## Context

- First independent slice of the parent row. `workspace-load-restore-next-call` reuses the DTO. The omitted-load default is separate because it needs bounded write-side restore behavior.
