# tool-error-handler-classification-complexity-extraction — Simplify tool-error classification

**row:** `tool-error-handler-classification-complexity-extraction` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:230-356`

## Acceptance

- [ ] `ClassifyError`, `TryClassifyBindingLike`, and every new or changed helper measure cyclomatic complexity at or below 8.
- [ ] Focused stages preserve binding, reload-race, registered-handler dictionary order, and fallback precedence.
- [ ] Preserve workspace-eviction and confirmed-not-found exclusions plus all categories, messages, parameter hints, schema hints, and unexpected-error inner-chain behavior.
- [ ] All named parameter-validation, reload-race, not-found, observability, and stale-token regressions pass.

## Evidence

- Read-side Roslyn metrics on 2026-07-17 measured `ClassifyError` at CC 11 and `TryClassifyBindingLike` at CC 9.

## Validation

- Run `ToolErrorHandlerParameterValidationTests`, `ToolErrorHandlerWorkspaceReloadRaceTests`, `WorkspaceReloadedNotFoundConflationTests`, `ErrorResponseObservabilityTests`, and `PreviewTokenStaleAcrossAutoReloadTests` without modifying them unless a behavior-preserving regression gap is demonstrated.

## Dependencies

- None.
