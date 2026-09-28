# tool-binding-missing-parameter-named — A missing required tool argument is reported as 'arguments' or '<unknown>'

**row:** `tool-binding-missing-parameter-named` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredResultProjector.cs:60-100`
- `src/RoslynMcp.Host.Stdio/Middleware/UnknownArgumentDetector.cs:23-113`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs:43-67`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs:19-30`
- (new) `tests/RoslynMcp.Tests/ToolBindingMissingParameterWireTests.cs`

## Acceptance

- [ ] When a call fails and the supplied arguments lack a name in the tool's input-schema `required` list, the envelope names every missing required parameter, never `'arguments'` or `'<unknown>'`. Such a call cannot have bound, so the failure is a binding failure whatever exception type the SDK used.
- [ ] The lookup lives in the result projector's error path. `StructuredResultProjector.cs` has the request arguments (`context.Params?.Arguments`, `:63`) and the call context that resolves the matched tool. `ToolErrorHandler.ClassifyError` (`ToolErrorHandler.cs:329`) has neither, so it only receives the resolved parameter name. The schema reading reuses `UnknownArgumentDetector`'s tool resolution and `ProtocolTool.InputSchema` reader (`:44-52`, `:82-113`), extended to read `required`, instead of a second copy.
- [ ] The lookup does not parse the SDK's exception message, so it stays valid after `tool-error-handler-argument-sniffing-arms-retired` collapses `BuildSafeArgumentMessage` to its marker check.
- [ ] Red-first wire test through the real SDK binding path, not a hand-built exception: a tool call that omits one required parameter names it in the envelope. Keep the unit tests that hand-build the binder exception with `paramName: "path"` (`StructuredCallToolFilterTests.cs:49-51`, `StructuredCallToolFilterElicitationTests.cs:21-23`) only if they still describe what the SDK actually throws; otherwise rewrite them to the real shape.

## Evidence

- Binding-like failures route through `_bindingLikeHandlers` (`ToolErrorHandler.cs:176-200`). `ArgumentNullException` renders `ParamName ?? "<unknown>"` (`:185`), and `ArgumentException` falls to `BuildSafeArgumentMessage`, whose fallback prints `Parameter '{ParamName ?? "<unknown>"}' is invalid` (`:570-572`, `:646`). The caller sees only whatever name the binder put in `ParamName`, and `ClassifyError(ex, toolName, correlationId)` (`:329`) cannot check it against the request.
- Retro 2026-09-27 envelopes named `'arguments'` or `'<unknown>'` for missing required parameters. The existing unit tests (`StructuredCallToolFilterTests.cs:49-51`, `StructuredCallToolFilterElicitationTests.cs:21-23`) hand-build the binder exception with `paramName: "path"`, so they pass while the real path does not name the parameter.

## Context

- Split out of the retro issue `generic-error-redaction-hides-refusal-reason`; the throw-site half is `tool-refusal-public-message-guard`. This half needs no guard and ships independently.
- `tool-error-handler-argument-sniffing-arms-retired` (on main since #1654) rewrites `BuildSafeArgumentMessage`; this row does not touch it.
- Related Low row `schema-hint-truncates-on-abbreviation` edits the schemaHint helper in `ToolErrorHandler.cs` (`:788`); different mechanism, not folded in.
- Size M: two production files (the projector and the detector's shared schema reader).
