# logging-failure-records-lack-diagnosis — Put error category and sanitized frames in tool-failure log records

**row:** `logging-failure-records-lack-diagnosis` · **pri:** `Medium` · **size:** `M` · **deps:** `logging-jsonl-drops-structured-state`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredDispatchPipeline.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredResultProjector.cs`
- `src/RoslynMcp.Core/Services/PublicExceptionDetailPolicy.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] Tool-failed records carry `errorCategory` (and `paramName` for InvalidArgument) as fields.
- [ ] Unexpected-failure records carry the top N frames as `Type.Method` symbol names only — no file paths, no exception messages, no argument values.
- [ ] Secret-sentinel test proves no message/path/argument text reaches the record.
- [ ] V2 recipe: one correlation-id query yields category + type + frames.

## Evidence

- Logging audit 20260930-1340 (dimension A4); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

V2: a failing find_references (unknown workspaceId) produced ONE log record — "Tool find_references failed; outcome=expected-error; elapsedMs=1" — while the client envelope carried category NotFound and the actionable message. An agent reading only the server log cannot tell which error category or parameter failed. Unexpected failures log exceptionTypes and stackFrameCount only (ServerObservability.cs:96-100): the agent learns THAT a stack exists, never where.

Depends on the fields serialization row.

**Approach:** Reuse ToolErrorHandler.ClassifyError output (already computed at dispatch) for category; build frame symbols from StackTrace.GetFrames() method metadata through the existing PublicExceptionDetailPolicy projection so the sanitization stays single-sourced.

**Counterargument:** The repo deliberately omits exception text and stacks from the sink (public artifact; logs land on customer machines). Type.Method names are code identifiers, not user data, so the policy holds; the cost is a slightly larger projection surface to keep secret-safe.
