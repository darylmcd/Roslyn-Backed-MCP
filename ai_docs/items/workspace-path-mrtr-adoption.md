# workspace-path-mrtr-adoption — Move workspace-path recovery to MRTR

**row:** `workspace-path-mrtr-adoption` · **pri:** `High` · **size:** `M` · **deps:** `mcp-mrtr-dispatch-contract`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` — pre-bind missing-argument inspection/retry boundary.
- New `tests/RoslynMcp.Tests/WorkspacePathMrtrWireTests.cs`.

## Acceptance

- [ ] An omitted `workspace_load.path` produces exactly one allowed form input request rather than an immediate binder error.
- [ ] An accepted sanctioned path is consumed from request-scoped input responses and the retried logical call succeeds once.
- [ ] Reject/cancel, unsanctioned paths, unexpected fields, and sensitive-field requests remain refused with stable sanitized outcomes.
- [ ] Parameterize the same flow for 2025-11-25 stateful compatibility and 2026-07-28 `server/discover` MRTR.
- [ ] Remove missing-parameter recovery that depends on `ArgumentException.ParamName` or parsing SDK exception prose.

## Evidence

- Raw clients advertising elicitation received no input request in either protocol era; the call immediately failed.
- SDK 2.1 reports the missing field through `ParamName = "arguments"`, so the current allowlist checks `workspace_load/arguments` instead of `workspace_load/path`.
