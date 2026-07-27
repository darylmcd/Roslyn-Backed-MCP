# workspace-fork-restore-error-redaction — Redact restore failure output

**row:** `workspace-fork-restore-error-redaction` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs` (`RestoreForkAsync`)
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`

## Acceptance

- [ ] Do not place raw, unbounded `dotnet restore` stdout/stderr in the exception returned to MCP clients.
- [ ] Surface exit code plus a bounded redacted diagnostic summary while retaining actionable detail in protected logs.
- [ ] Add a regression containing a credential-shaped feed URL and prove the client-facing error omits it.

## Evidence

- The 2026-07-26 ten-row remediation review found `RestoreForkAsync` concatenates raw command output into an `InvalidOperationException`, which can expose credential-bearing feed URLs.
