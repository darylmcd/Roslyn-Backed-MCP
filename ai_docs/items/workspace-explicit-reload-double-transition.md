# workspace-explicit-reload-double-transition — Refresh once for one explicit reload

**row:** `workspace-explicit-reload-double-transition` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:130` — explicit reload action within RunWriteAsync through136.
- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs:94` — public reload responses and transition controls at114.

## Acceptance

- [ ] Observe RED when stale physical source is explicitly reloaded: one request must refresh native documents and advance the workspace version exactly once before any intentional post-restore refresh.
- [ ] Preserve retained preview range through one refresh and expire it only after the actual configured number of transitions; cover stale/nonstale source and before/after-copy ordering controls.
- [ ] Correct the explicit caller while preserving ordinary automatic write staleness policy, load/write gates, intentional post-restore refresh, cancellation and response shape. Do not change timeouts or retained token range.
- [ ] Re-review the preserved correction and tests against current main, pass scoped/hosted qualification, then close.

## Evidence

- Main58ba4207 invokes the default write-gate staleness refresh, then explicit ReloadAsync, producing two transitions for one stale request.
- `e2-reload-current-state-old.log`:2 failed/0 passed at the single-transition oracle. Four-control OLD log:3 failed/1 passed; two stale cases expected2/actual3 are genuine duplicate refresh, the third failure is an independent old fork strict-version guard.
- Immutable PR1826 head0062d60ca47a0b08d56d1c91feac333b67de876a preserves the explicit caller correction applyStalenessPolicy:false; it is not merged or qualified on main.
- Receipts and intake: `<user>/.codex/scratch/roslyn-remediate-20261023-split/restore-closeout-independent-intake.md`.

## Context

- Independent nonblocking remainder; no dependency from restore-callers-missing-packages-path.
- Read-only mechanism context: WorkspaceExecutionGate automatic pre-action policy at245–247 and native reload487. Do not alter the gate-wide policy to repair this caller.
