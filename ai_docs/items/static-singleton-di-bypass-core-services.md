# static-singleton-di-bypass-core-services — Replace static singleton DI-bypass state with scoped services

**row:** `static-singleton-di-bypass-core-services` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/WorkspaceEvictedException.cs:151-223`
- `src/RoslynMcp.Core/Services/AmbientGateMetrics.cs:12-48`

## Acceptance

- [ ] WorkspaceEvictionRegistry and AmbientGateMetrics are DI-registered scoped services or carry documented rationale for remaining static, with any production-visible Reset() hook isolated to test-only code paths
- [ ] WorkspaceEvictionRegistry lives in its own file distinct from WorkspaceEvictedException.cs

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S02b-core-service-contracts::DG7-config-deps-ergo, S02b-core-service-contracts::DG2-cleanliness
