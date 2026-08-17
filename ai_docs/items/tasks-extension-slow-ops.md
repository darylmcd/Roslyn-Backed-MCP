# tasks-extension-slow-ops — Adopt the MCP tasks extension for slow operations

**row:** `tasks-extension-slow-ops` · **pri:** `Low` · **size:** `M` · **deps:** `stdio-shutdown-flush-transport-ownership,prompt-call-error-filter-boundary,workspace-resource-list-notification-semantics`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs` (`WithTasks(...)` wiring + `ModelContextProtocol.Extensions.Tasks` package)
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (workspace_load / workspace_warm)
- `src/RoslynMcp.Host.Stdio/Tools/` build + test_run tool files
- `tests/RoslynMcp.Tests/` (task lifecycle: create → poll → result; cancellation)

## Acceptance

- [ ] workspace_load, build_*, test_run offer task-augmented execution (deferred result + `tasks/get` polling) alongside existing progress notifications
- [ ] Cancellation propagates through the task path (reuse the hardened gate classification — commits 656efda2 / cadc7e42 / a61c2e2a)
- [ ] New public contract surface documented per product-contract tiering

## Evidence

- The server's slowest ops stream progress only; tasks are the spec-blessed shape for long-running MCP operations (SEP-2663, official extension under 2026-07-28) — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §4, §5

## Notes

- SDK 2.x is already pinned; the live blocker is the compatibility decision for the separately packaged Tasks extension.
