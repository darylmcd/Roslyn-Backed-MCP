| Field | Content |
|---|---|
| Route | split-child of preview-token-consumed-reason |
| Diagnosis | `ProjectMutationService.cs:579-586` writes/reloads then generically removes; failures at :589-593 must not become Applied. |
| Approach | Capture completion identity before write, confirm only after required write/reload succeeds and before ancillary tracking. Preserve failure/partial outcomes without Applied claims; use shared typed reason for inside-gate missing tokens. Complete red-first public envelope matrix across all three stores, apply_with_verify and fork: second apply, reload, TTL, wrong-store, unknown, retention and owner loss. Fork copies a preview and does not consume its source; do not label source Applied merely because fork succeeded. Only this final dependency-complete child closes the original row. |
| Scope | 1 production: `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`; 2 tests: `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`, `tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs`; docs: none.  Additional exact docs: `changelog.d/preview-token-project-confirmation.md`. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Shape C/A production ripple 1. ProjectMutationPreviewStore inherits shared required operations unchanged; no redundant wrapper. Re-probe after scaffold/fork peers land and include any actual changed callers. Parent row stays open until dependencies and final public proof land. |
| Validation | Observe new regressions fail on old code; per-edit compile_check/targeted test_run, documented CLI fallback after workspace-load failure; serialized just ci and required hosted Windows/Linux checks. |
| Performance review | Bound payload-free metadata count/TTL; verify atomic transitions and concurrency. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Project token reuse distinguishes successful application, reload, expiry and unknown tokens. |
| Backlog sync | Close rows: [preview-token-consumed-reason]. |
