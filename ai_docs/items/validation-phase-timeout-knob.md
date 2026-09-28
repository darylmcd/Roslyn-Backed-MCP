# validation-phase-timeout-knob — The 25 s validate_* phase cap is a hard-coded literal with no env knob

**row:** `validation-phase-timeout-knob` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:20-95`
- `src/RoslynMcp.Roslyn/Services/ValidationServiceOptions.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs:240-262`
- `ai_docs/references/environment-variables.md`
- `tests/RoslynMcp.Tests/WorkspaceValidationTimeoutTests.cs`

## Acceptance

- [ ] The per-phase cap moves from `DefaultValidationPhaseTimeout` (`WorkspaceValidationService.cs:25`) to a `ValidationServiceOptions` property with the same 25 s default, beside `GitStatusTimeout`. The public constructor passes it through (`:63` passes the literal today). The internal test constructor keeps its explicit `validationPhaseTimeout` parameter.
- [ ] `Program.cs` `BindValidationServiceOptions` binds an env override (for example `ROSLYNMCP_VALIDATION_PHASE_TIMEOUT_SECONDS`, positive integers only, like the other knobs there). `ai_docs/references/environment-variables.md` documents it with its default and names the phases it bounds.
- [ ] The timeout warning text (`CreateTimeoutResult`, `:752-808`) names the knob, so a caller who hits the cap knows what to raise. It keeps the phase name and the `retryable=true` token that ADR 0010 and the existing tests pin (`WorkspaceValidationTimeoutTests.cs:76-78`, `ValidateRecentGitChangesTests.cs:369-371`).
- [ ] Tests: the options default is 25 s; a service built from options with a short override times out the compile phase with `overallStatus: timeout` (extend `WorkspaceValidationTimeoutTests`, which pins the phase cap through the internal constructor today).
- [ ] Compatibility class: minor-compatible, so the row ships on the 4.x line. The knob is additive configuration, its default keeps today's 25 s, and the `validate_*` tools it bounds are experimental (`ServerSurfaceCatalog.Analysis.cs:34-35`).

## Evidence

- At `02db6c49`, `WorkspaceValidationService.cs:25` declares `DefaultValidationPhaseTimeout = TimeSpan.FromSeconds(25)`, and the DI constructor passes it at `:63`. `RunValidationPhaseAsync` arms it for every phase (`:741`). No env var or options entry reaches it, while the sibling `GitStatusTimeout` is bound from `ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS` (`Program.cs:247`, `:259-260`).
- Found by the round-2 cold review of PR #1655 (`gated-build-test-operation-deadline`), which leaves the 25 s cap on the compile, diagnostics and related-test discovery phases.

## Context

- `gated-build-test-operation-deadline` moves the `validate_*` test phase off this cap onto `TestTimeout`; this row only makes the remaining cap configurable. The two rows both edit `WorkspaceValidationService.cs`; sequence by hotspot.
