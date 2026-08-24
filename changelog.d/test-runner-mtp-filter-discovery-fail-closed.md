---
category: Fixed
---

- **Fixed:** TUnit/MTP filter translation now always requires discovery-backed test names, including direct `TestRunnerService` construction; the shared in-memory MCP harness owns hosted roots lifecycles and initialization-failure cleanup; fixture teardown reports failures; and reusable workspace/gate doubles fail closed. Closes `test-runner-mtp-filter-discovery-fail-closed`, `mcp-roots-fixture-lifecycle-consolidation`, `workspace-manager-test-double-consolidation`, `workspace-gate-test-double-consolidation`, `analysis-fixture-workspace-close-observability`, and `type-extraction-test-cleanup-observability`.
