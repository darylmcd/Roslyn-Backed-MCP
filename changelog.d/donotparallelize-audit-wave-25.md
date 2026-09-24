---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `SemanticGrepServiceTests`, `SemanticSearchFallbackTests`, and `ServerDiscoveryWireTests.DeprecatedAlias_ToolsListAndOmittedWorkspaceDispatchMatchCanonical` — the first two only read the shared sample workspace through the synchronized `WorkspaceIdCache`, and the wire test loads into its own private `WorkspaceManager`, so all three opt-outs were removed with source-adjacent rationale, proven safe by repeated concurrent test runs.
