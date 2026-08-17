# structured-tool-dispatch-adapter — Reuse read dispatch for structured tools

**row:** `structured-tool-dispatch-adapter` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs`
- `tests/RoslynMcp.Tests/Services/WorkspaceDriftServiceTests.cs`

## Acceptance

- [ ] Add a generic/result-projecting read-by-workspace dispatch path that preserves gate and cancellation semantics.
- [ ] Migrate `workspace_drift_check` off its one-off gate/service lambda without forwarding wrappers or serialization in `ToolDispatch`.
- [ ] Correct `ToolDispatch` ownership comments so every pure read path follows the declared adapter contract.
- [ ] One dispatch regression proves the structured result, workspace id, and cancellation token pass through unchanged.

## Evidence

- `ToolDispatch.ReadByWorkspaceIdAsync` is hard-wired to `Task<string>`, so the first explicit `CallToolResult` producer duplicated its gate/service/project flow and made the file's “every pure read shim” claim false.
