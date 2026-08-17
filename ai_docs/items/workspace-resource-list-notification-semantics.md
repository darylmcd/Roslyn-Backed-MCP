# workspace-resource-list-notification-semantics — Stop false resource-list change notifications

**row:** `workspace-resource-list-notification-semantics` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`
- New `tests/RoslynMcp.Tests/WorkspaceResourceListNotificationWireTests.cs`.

## Acceptance

- [ ] Snapshot `resources/list` before and after workspace load, reload, and close; emit no `notifications/resources/list_changed` when the advertised list is byte-equivalent.
- [ ] Remove fire-and-forget notification calls and their catch-all failure suppression from the current static-resource lifecycle.
- [ ] One table-driven legacy/2026 wire test covers load/reload/close and observes zero list-changed frames; do not encode a hypothetical future subscription policy in this row.

## Evidence

- Resource registrations and URI templates are static, but all three workspace lifecycle tools currently fire the list-changed notification unconditionally.
- The helper discards the returned task at each caller and catches every send exception internally.
