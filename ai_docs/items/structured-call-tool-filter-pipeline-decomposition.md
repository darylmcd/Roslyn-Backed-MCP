# structured-call-tool-filter-pipeline-decomposition — Decompose the structured call filter pipeline

**row:** `structured-call-tool-filter-pipeline-decomposition` · **pri:** `Medium` · **size:** `M` · **deps:** `workspace-auto-load-on-demand-design,structured-tool-dispatch-adapter,tool-error-envelope-sensitive-detail-disclosure`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` — `Create` and its retry/recovery helpers.
- New internal collaborators under `src/RoslynMcp.Host.Stdio/Middleware/`.
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs`
- `tests/RoslynMcp.Tests/StructuredToolRawWireIntegrationTests.cs`

## Acceptance

- [ ] Keep `Create` as the composition boundary while moving workspace resolution, elicitation/retry orchestration, and result/error projection into focused internal collaborators.
- [ ] Preserve tool names, argument mutation rules, error envelopes, `_meta`, structured content, cancellation, and protocol-version behavior.
- [ ] A table-driven regression matrix exercises explicit/single/zero/multiple workspace resolution plus success, binder failure, handler failure, and elicitation retry paths.
- [ ] No forwarding-only duplicate pipeline or second error-classification source remains.

## Evidence

- 2026-08-16 remediation review measured `StructuredCallToolFilter.Create` at cyclomatic complexity 14, 136 LOC, nesting depth 5, and maintainability index 32.49.
