# workspace-load-shutdown-publication-race — Fence session publication against shutdown

**row:** `workspace-load-shutdown-publication-race` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:445` — asynchronous session load, publication/watch468–475, stop1215–1230 and shutdown1261–1270.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceLifecycleTests.cs`

## Acceptance

- [ ] Pause a real independent load at an existing loader seam and cover both load-publication/shutdown orders; observe causal RED before changing admission.
- [ ] Prevent post-terminal session/watch publication and ensure exactly one cleanup and slot outcome for cancellation, failure and successful load.
- [ ] Preserve ordinary host/load/fork lifetime controls without synchronous joins or a new controller.
- [ ] Complete fresh review and affected/hosted qualification before closure.

## Evidence

- Main58ba4207 publishes sessions/watches after awaiting LoadIntoSessionAsync without a lifecycle admission fence at publication; stop/shutdown separately sets disposal state and snapshots/clears sessions.
- The admission gap is code-derived; post-shutdown publication has not been runtime-reproduced in this session.

## Context

- Independent nonblocking follow-up; no restore closure dependency. Re-derive actual stop/load token behavior and final publication fence before implementation.
