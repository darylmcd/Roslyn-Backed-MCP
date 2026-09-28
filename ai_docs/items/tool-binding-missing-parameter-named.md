# tool-binding-missing-parameter-named — A missing required tool argument is reported as 'arguments' or '<unknown>'

**row:** `tool-binding-missing-parameter-named` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredResultProjector.cs:25-170`
- `src/RoslynMcp.Host.Stdio/Middleware/UnknownArgumentDetector.cs:23-113`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs:43-67`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs:19-30`
- (new) `tests/RoslynMcp.Tests/ToolBindingMissingParameterWireTests.cs`

## Acceptance

- [ ] Trigger: the SDK dispatch (`next`, `StructuredDispatchPipeline.cs:168`) throws, and the arguments it received lack a name in the tool's input-schema `required` list. The envelope then names every missing required parameter, never `'arguments'` or `'<unknown>'`. The arguments checked are the ones the binder saw: after alias normalization and `workspaceId` auto-resolution, which can add keys. Such a call cannot have bound, so the failure is a binding failure whatever exception type the SDK used.
- [ ] Scope: the lookup lives only in the catch around that dispatch (`StructuredResultProjector.cs:74-96`), which has the request arguments (`context.Params?.Arguments`) and the tool name. Early-terminal outcomes never reach it: the workspace resolver's fast-fail and auto-load failures (`ProjectEarlyExceptionResult`, `:141-152`) and the elicitation recovery results (`ProjectRecoveredResult`, `:126-139`). The shared private `BuildErrorResult` (`:154-170`) is therefore not the hook. An exception that already carries a public message is never relabelled. Today those are `PublicArgumentException` and `PublicInvalidOperationException`; after `public-argument-exception-core-move` it is the `IPublicMessageException` marker.
- [ ] `ToolErrorHandler.ClassifyError` (`ToolErrorHandler.cs:329`) has neither the arguments nor the schema, so it only receives the resolved parameter name. The schema reading reuses `UnknownArgumentDetector`'s tool resolution and `ProtocolTool.InputSchema` reader (`:44-52`, `:82-113`), extended to read `required`, instead of a second copy.
- [ ] The lookup does not parse the SDK's exception message, so it stays valid after `tool-error-handler-argument-sniffing-arms-retired` collapses `BuildSafeArgumentMessage` to its marker check.
- [ ] Red-first wire test through the real SDK binding path, not a hand-built exception: a tool call that omits one required parameter names it in the envelope.
- [ ] Regression test in the same wire file. With two workspaces loaded, `validate_workspace` (schema-required `workspaceId`, auto-resolve eligible under `ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor`) called without `workspaceId` returns the resolver's fast-fail envelope unchanged: category `InvalidArgument`, parameter `workspaceId`, and a message that lists the candidates (`StructuredWorkspaceResolver.cs:129-139`, `:217-220`). `ToolCallErrorWireContractTests.WorkspaceFastFails_UseEraSpecificWireShape` (`:176`) stays green unchanged.
- [ ] Keep the unit tests that hand-build the binder exception with `paramName: "path"` (`StructuredCallToolFilterTests.cs:49-51`, `StructuredCallToolFilterElicitationTests.cs:21-23`) only if they still describe what the SDK actually throws; otherwise rewrite them to the real shape.
- [ ] Compatibility class: minor-compatible under `docs/release-policy.md:19`, so the row ships on the 4.x line. The category stays the one the exception maps to today, and every binding-like handler returns `InvalidArgument` (`ToolErrorHandler.cs:176-206`). The envelope gains no field. Only the message and the parameter name behind `schemaHint` change, from the binder's `arguments` or `<unknown>` to the real name.

## Evidence

- Binding-like failures route through `_bindingLikeHandlers` (`ToolErrorHandler.cs:176-200`). At `02db6c49`, `ArgumentNullException` renders `ParamName ?? "<unknown>"` (`:185`), and `ArgumentException` falls to `BuildSafeArgumentMessage`, whose fallback prints `Parameter '{ParamName ?? "<unknown>"}' is invalid` (`:561-563`, `:637`). The caller sees only whatever name the binder put in `ParamName`, and `ClassifyError(ex, toolName, correlationId)` (`:329`) cannot check it against the request.
- Retro 2026-09-27 envelopes named `'arguments'` or `'<unknown>'` for missing required parameters. The existing unit tests (`StructuredCallToolFilterTests.cs:49-51`, `StructuredCallToolFilterElicitationTests.cs:21-23`) hand-build the binder exception with `paramName: "path"`, so they pass while the real path does not name the parameter.
- A missing `workspaceId` is not always a binding failure. With two or more workspaces loaded, the resolver fast-fails a read-only call before binding with a `PublicArgumentException` that lists the candidates (`StructuredWorkspaceResolver.cs:129-139`), and that exception reaches the same private `BuildErrorResult` through `ProjectEarlyExceptionResult` (`StructuredResultProjector.cs:141-152`, `StructuredDispatchPipeline.cs:130-139`). A lookup placed there would relabel it and drop the candidate list.

## Context

- Split out of the retro issue `generic-error-redaction-hides-refusal-reason`; the throw-site half is `tool-refusal-public-message-guard`. This half needs no guard and ships independently.
- `tool-error-handler-argument-sniffing-arms-retired` (on main since #1654) rewrites `BuildSafeArgumentMessage`; this row does not touch it.
- Related Low row `schema-hint-truncates-on-abbreviation` edits the schemaHint helper in `ToolErrorHandler.cs` (`TrimToFirstSentence`, `:782` at `02db6c49`); different mechanism, not folded in.
- Size M: two production files (the projector and the detector's shared schema reader).
