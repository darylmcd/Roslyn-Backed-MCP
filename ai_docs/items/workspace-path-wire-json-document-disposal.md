# workspace-path-wire-json-document-disposal — Dispose parsed wire-test documents

**row:** `workspace-path-wire-json-document-disposal` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/WorkspacePathMrtrWireTests.cs` — five payload projections use `JsonDocument.Parse(...).RootElement` without disposing the owning document.

## Acceptance

- [ ] Retain each parsed document in a scoped `using` declaration while its root is consumed.
- [ ] Preserve all wire assertions and avoid returning a root element beyond its document lifetime.
- [ ] Run `WorkspacePathMrtrWireTests` across both supported protocol versions.

## Evidence

- 2026-09-11 independent cold review of workspace-load argument recovery found five pre-existing unowned document lifetimes; the new malformed-argument regression already disposes its documents correctly.
