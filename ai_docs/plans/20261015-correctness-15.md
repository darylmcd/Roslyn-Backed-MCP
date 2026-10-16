# Correctness remediation - 15 items

<!-- scope: in-repo -->
<!-- artifact: plan -->
<!-- status: active -->

| Field | Decision |
|---|---|
| Base | Clean main000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7 equals origin/main; zero open PRs after fetch. |
| Existing work | Preserve clean extraction8a40da79 and restoreea969292 checkpoints and explicit deferred execution holds. Do not claim either complete. |
| Workflow | Direct implementation; disjoint delegated worktrees; serialized local runtime gates. Do not use backlog-remediate skill. |
| Compatibility | Preserve published shapes, categories and exception identities. Improve correctness and safe authored diagnostics. |

| # | Row | Complete fix |
|---|---|---|
| 1 | cohesion-handwritten-partial-method-inclusion | Analyze handwritten partial implementations once; preserve generator exclusions. |
| 2 | cohesion-direct-method-call-connectivity | Connect caller and called method by semantic identity; preserve overload separation. |
| 3 | shared-members-explicit-generic-call-traversal | Traverse explicit/inferred generic calls and compare original method identities. |
| 4 | cohesion-ranked-limit-after-discovery | Complete discovery before severity ranking and limit; preserve incomplete/cancelled outcomes. |
| 5 | suggestion-parameter-object-tool-guidance | Recommend registered parameter-object preview/apply workflow. |
| 6 | bounded-store-capacity-atomic-insertion | Make insertion/eviction atomic; preserve TTL/invalidation and newest-token retention. |
| 7 | feature-provider-constructor-cancellation-unwrapping | Preserve reflected constructor cancellation through both loader boundaries. |
| 8 | consumer-analysis-cancellation-returns-partial | Throw on cancellation in both consumer traversal loops. |
| 9 | change-tracker-subscriber-failure-isolation | Notify every subscriber in order despite preceding subscriber failures. |
| 10 | workspace-validation-explicit-empty-tracker-fallback | Preserve explicit empty scope; reserve tracker/Git fallback for omitted scope. |
| 11 | restore-assets-read-failure-false-fresh | Require restore for known package expectations with unusable assets; distinguish metadata evaluation failure. |
| 12 | workspace-close-drain-descendant-check-ignores-case-on-linux | Use filesystem path identity and prove Linux sibling/descendant behavior. |
| 13 | symbol-search-pagination-integer-overflow | Prevent wrapped scan/continuation arithmetic and retain ordinary pages. |
| 14 | test-run-service-refusals-public-message | Publish safe actionable caller refusals; retain stable wire categories and internal detail ownership. |
| 15 | test-run-zero-match-filter-silent-success | Warn when a supplied filter reports zero tests; preserve truthful process/count data and other warnings. |

1. Re-derive source and same-mechanism references before editing.
2. Observe meaningful regressions fail on old behavior; preserve successful and hostile-input controls.
3. Run focused tests serially, integrate reviewed source, and inspect final diff.
4. Run unsharded just ci on final tree; fix every introduced/exposed defect without weakening gates.
5. Require exact-head hosted Windows/Linux/SDK-floor and CodeQL green; merge and run canonical cleanup.
6. Verify clean current main and no open PRs; preserve only explicitly deferred prior checkpoints.
7. backlog: sync ai_docs/backlog.md through the canonical writer in the implementation PR.
