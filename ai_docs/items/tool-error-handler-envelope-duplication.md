# tool-error-handler-envelope-duplication — extract shared error-envelope builder in ToolErrorHandler.FormatErrorResponse

**row:** `tool-error-handler-envelope-duplication` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:510`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:527`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:545`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:567`
- `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs`

## Acceptance

- [ ] The four anonymous-object envelope literals in `FormatErrorResponse` collapse into one builder that emits the base fields (`error`/`category`/`tool`/`message`/`exceptionType`) plus at most one optional structured field.
- [ ] `ErrorResponseObservabilityTests` continue to pass byte-for-byte on the `closestMatches`, `blockingDependencies`, `schemaHint`, and bare-envelope shapes.

## Evidence

- Code-quality review of PR #1138 (`extract-type-preview-refusal-missing-blocking-deps`): the `blockingDependencies` branch added by that PR is the fourth copy of an identical five-property anonymous-object literal; the `InternalError`, `closestMatches`, and `schemaHint` branches already repeat it verbatim.

## Context

Spin-off from the `extract-type-preview-refusal-missing-blocking-deps` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1138). Every future structured error field costs another copy of this literal until consolidated.
