---
category: Fixed
---

- **Fixed:** Host lifecycle ownership now leaves DI responsible for workspace teardown, and one process-start metadata source feeds recycle state, `server_info`, and `server_heartbeat` with secret-safe fallback reporting.
- **Fixed:** Preview redemption restores request-scoped client-root narrowing, removed-project documents participate in write-set revalidation, and atomic temp-file cleanup diagnostics retain the ambient correlation id.
- **Fixed:** Workspace close treats removal as the commit point and bounds post-commit process cleanup while reporting cancellation or failure once through the safe diagnostic seam.
- **Fixed:** Read-tool pagination, limit, bulk, and required-field validation now precedes workspace dispatch across the affected analysis and symbol endpoints.
- **Fixed:** Interface-member implementation lookup is shared across reference, signature-change, parameter-object, override-base, and unused-symbol analysis paths.
- **Fixed:** Unique zero-workspace discovery is covered through the registered `workspace_load` wire path, including cancellation and missing-id fail-closed behavior.

Closes `host-shutdown-di-owned-workspace-disposal`, `server-start-time-source-consolidation`, `workspace-close-postcommit-drain-contract`, `symbol-interface-implementation-lookup-consolidation`, `atomic-file-cleanup-correlation-id-reporter-seam`, `tool-dispatch-preview-token-review-followups`, `workspace-dispatch-parameter-validation-hoist-siblings`, and `workspace-auto-load-registered-dispatch-wire-coverage`.
