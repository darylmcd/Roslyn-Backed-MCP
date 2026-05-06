# Backlog sweep plan — 20260506T211152Z
<!-- purpose: Backlog sweep plan for the remaining 2026-05-06 Medium initiative batch. -->
<!-- generated_at: 2026-05-06T21:11:52Z -->
<!-- backlog_snapshot: 2026-05-06T21:07:03Z -->

## Summary

This follow-on plan consumes the three remaining actionable Medium backlog rows after the 20260505T191730Z sweep completed. Deferred / Low rows remain parked because their row text explicitly requires external evidence or future trigger conditions.

## Guardrails

- Each initiative is one coherent backlog row.
- Rule 3 sizing target: ≤4 production files per initiative.
- Rule 4 sizing target: ≤3 test files per initiative.
- Tool policy: edit-only from the main checkout; no Roslyn apply tools.
- Backlog sync: remove rows only in the PR that ships the corresponding implementation.

## Initiatives

### 1. validate-workspace-markdown-formatter-decomposition

| Field | Content |
|---|---|
| Status | merged (PR #533, 2026-05-06) |
| Backlog rows closed | `validate-workspace-markdown-formatter-decomposition` |
| Diagnosis | `ValidateWorkspaceMarkdownFormatter.Format` owns the verdict line plus metrics, diagnostics, discovered tests, failures, rerun-filter, and warning table branches in one method. The current design is defensible because the formatter is a small pure function with all output logic in one place; extraction still wins because the method is already a complexity hotspot and response-format changes now require reviewing unrelated table branches. |
| Approach | Extract private section writers and small table helpers while preserving byte-stable markdown output. |
| Scope | Production files: 1 — `src/RoslynMcp.Host.Stdio/Formatters/ValidateWorkspaceMarkdownFormatter.cs`. Test files: 1 existing — `tests/RoslynMcp.Tests/ValidateWorkspaceMarkdownFormatterTests.cs` as byte-stability guard. |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | A helper could change whitespace, truncation wording, or escaped table-cell output. Keep tests focused on exact output snippets. |
| Validation | `mcp__roslyn__compile_check`; focused `ValidateWorkspaceMarkdownFormatterTests`; `./eng/verify-release.ps1 -Configuration Release -NoCoverage`; vulnerability check. |
| CHANGELOG category | Changed |
| Backlog sync | Close rows: [`validate-workspace-markdown-formatter-decomposition`]. Mark obsolete: []. Update related: []. |

### 2. test-discovery-service-complexity-refactor

| Field | Content |
|---|---|
| Status | merged (PR #535, 2026-05-06) |
| Backlog rows closed | `test-discovery-service-complexity-refactor` |
| Diagnosis | `FindRelatedTestsForFilesAsync` and `CollectFallbackMatchesAsync` are large branch-heavy methods. The current design is defensible because the code keeps the test-discovery workflow linear and local; extraction still wins because the methods are measured complexity hotspots and future edge cases will otherwise keep landing inside the same two blocks. |
| Approach | Extract cohesive helpers around input normalization, direct reference matching, fallback matching, and heuristic construction without changing public behavior. |
| Scope | Production files: 1 — `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`. Test files: existing `ValidationToolsIntegrationTests` and `HardeningBehaviorTests`; no new tests if behavior remains structural. |
| Tool policy | edit-only |
| Estimated context cost | 40000 |
| Risks | Accidentally changing dedupe order, max-result truncation, or cancellation propagation. Focused test-discovery fixtures must remain green. |
| Validation | `mcp__roslyn__compile_check`; focused `ValidationToolsIntegrationTests` test-discovery cases plus hardening path; `./eng/verify-release.ps1 -Configuration Release -NoCoverage`; vulnerability check. |
| CHANGELOG category | Changed |
| Backlog sync | Close rows: [`test-discovery-service-complexity-refactor`]. Mark obsolete: []. Update related: []. |

### 3. scaffold-test-preview-sampled-test-names

| Field | Content |
|---|---|
| Status | in-review |
| Backlog rows closed | `scaffold-test-preview-sampled-test-names` |
| Diagnosis | `scaffold_test_preview` always emits `<TargetMethodName>_Needs_Test`, pushing naming work back to the outer agent. The current design is defensible because deterministic placeholders avoid model-dependent behavior and work for clients without sampling; a gated `useSampling=false` default preserves that behavior while letting capable clients collapse rename loops. |
| Approach | Add a nullable sampling path to `ScaffoldingTools.PreviewScaffoldTest` and `IScaffoldingService.PreviewScaffoldTestAsync`; when enabled and sampling succeeds, replace the placeholder with a sanitized sampled Given/When/Then name. |
| Scope | Production files: up to 4 — `src/RoslynMcp.Core/Services/IScaffoldingService.cs`, scaffold DTO location, `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`, `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`. Test files: up to 2, centered on disabled/default behavior and enabled sampled-name behavior. |
| Tool policy | edit-only |
| Estimated context cost | 60000 |
| Risks | SDK sampling call shape, capability absence, and invalid sampled identifiers. Default path must preserve placeholder output exactly; sampling failures should degrade to placeholder without hiding real non-sampling errors. |
| Validation | `mcp__roslyn__compile_check`; focused scaffolding tests; `./eng/verify-release.ps1 -Configuration Release -NoCoverage`; vulnerability check. |
| CHANGELOG category | Added |
| Backlog sync | Close rows: [`scaffold-test-preview-sampled-test-names`]. Mark obsolete: []. Update related: []. |

## Skipped Rows

| id | Reason |
|---|---|
| `tool-surface-pagination-or-tool-sets` | Low / track-only; row requires small-model friction or ~200-tool threshold before implementation. |
| `validate-locator-preflight-tool` | Defer; re-measure after 2026-05-12. |
| `http-streamable-host-project` | Defer; requires concrete remote-deployment driver and design. |
| `workspace-process-pool-or-daemon` | Defer; OrchardCore evidence does not justify implementation. |

## Final Todo

- backlog: sync ai_docs/backlog.md
