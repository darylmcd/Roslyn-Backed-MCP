| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | Each anchored file carries `[DoNotParallelize]` (grep-confirmed at selection; `ServerInfoPathBoundaryTests.cs` carries 2). Classes reading only through the synchronized `WorkspaceIdCache` with no shared mutable state are removal candidates; classes mutating shared server-side, static, environment, or process state (workspace reload, `*_apply`, static caches, env vars, child `dotnet` processes against shared fixtures) keep serialization. Per-file decision made against each file body (and its base classes) during implementation. |
| Approach | Row Acceptance verbatim, per anchored file (`tests/RoslynMcp.Tests/SemanticGrepServiceTests.cs`, `tests/RoslynMcp.Tests/SemanticSearchFallbackTests.cs`, `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`): (1) for every `[DoNotParallelize]`, either remove it after repeated concurrent evidence or retain it with a source-adjacent comment naming the concrete process-global/shared-workspace dependency; (2) run each affected class repeatedly before and after the decision — a retained opt-out cites the observed failure mechanism, a removed opt-out stays green across the bounded repetition (>=3x); (3) do not weaken assertions, add sleeps, or classify unrelated concurrency failures as success. |
| Scope | Test files (3): `tests/RoslynMcp.Tests/SemanticGrepServiceTests.cs`, `tests/RoslynMcp.Tests/SemanticSearchFallbackTests.cs`, `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`. No production files. |
| Tool policy | `edit-only` |
| Estimated context cost | 28000 |
| Risks | A wrongly cleared class surfaces as an intermittent CI failure later; mitigated by >=3x repeated-run evidence and inspecting base classes, env-var mutation, and child-process use. Rationale comments must state the dependency, not run history. Sibling waves touch disjoint file sets. Fanout probe: test-only, 0 production ripple. |
| Validation | Per class: `test_run --filter "FullyQualifiedName~<ClassName>"` (or `dotnet test --filter`) repeated >=3x, then with wave siblings concurrently; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Re-audited `[DoNotParallelize]` opt-outs on `SemanticGrepServiceTests.cs`, `SemanticSearchFallbackTests.cs`, `ServerDiscoveryWireTests.cs` — removed opt-outs proven safe by repeated concurrent runs and documented retained ones with their concrete shared-state dependency. |
| Backlog sync | Close rows: [donotparallelize-audit-wave-25]. Mark obsolete: []. |
