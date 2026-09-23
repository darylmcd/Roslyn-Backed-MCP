# Decisions

Architecture decision records (ADRs) for breaking, compatibility, and security decisions on the public surface. Breaking changes require an ADR here plus a `CHANGELOG.md` migration note (see [release-policy.md](../release-policy.md)).

| File | Purpose | Status |
|------|---------|--------|
| [0001-locationdto-nested-field-migration.md](0001-locationdto-nested-field-migration.md) | Additive nested `Location` field on `SymbolDto`/`DiagnosticDto`/`TypeUsageDto`, legacy-flat-field deprecation window, and the bounded producer/consumer migration stages | Accepted 2026-08-06 |
| [0002-configured-sanctioned-root-boundary.md](0002-configured-sanctioned-root-boundary.md) | Server-owned sanctioned-root security boundary, canonical path resolution, and migration from client Roots authority | Accepted 2026-08-13 |
| [0003-sdk-2x-wire-compatibility.md](0003-sdk-2x-wire-compatibility.md) | SDK 1.4.1→2.1.0 lineage, dual-protocol wire contract, correction classification, and consumer migration | Accepted 2026-08-14 |
| [0004-public-command-diagnostic-path-projection.md](0004-public-command-diagnostic-path-projection.md) | Secret-safe public projection of child-process diagnostic paths across Windows, UNC, and POSIX forms | Accepted 2026-08-22 |
| [0005-stable-profile-preview-apply-closure.md](0005-stable-profile-preview-apply-closure.md) | Stable-profile preview/apply closure, registration invariants, and token redemption | Accepted 2026-08-22 |
| [0006-modelcontextprotocol-2-2-servicing.md](0006-modelcontextprotocol-2-2-servicing.md) | ModelContextProtocol 2.1.0→2.2.0 servicing, stdio-only scope, and non-breaking compatibility disposition | Accepted 2026-08-24 |
| [0007-tasks-extension-compatibility.md](0007-tasks-extension-compatibility.md) | Exact Tasks/core 2.2.0 adoption posture, per-request opt-in, synchronous fallback, finite retention, and process-lifetime handles | Accepted 2026-09-04 |
| [0008-workspace-id-optional-adoption.md](0008-workspace-id-optional-adoption.md) | Measured optional `workspaceId` adoption, NO-GO expansion decision, retired flip batches, and concrete recheck trigger | Accepted 2026-09-04 |
| [0009-tool-surface-policy.md](0009-tool-surface-policy.md) | Tool consolidation risk buckets and compatibility aliases | Accepted 2026-09-04 |
| [0010-validation-verdict-completeness.md](0010-validation-verdict-completeness.md) | Incomplete compilation verdicts and retryable test-phase timeouts | Accepted 2026-09-15 |
