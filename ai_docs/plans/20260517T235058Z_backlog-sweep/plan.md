# Backlog sweep plan — 20260517T235058Z

**Generated:** 2026-05-17T23:50:58Z
**Backlog snapshot:** 2026-05-17T15:49:13Z
**Mode:** `/backlog-sweep:prepare count=20`
**Initiative count:** 20 selected (8 High + 12 Medium)
**Phase:** skeleton (deepener stanzas pending)

## Plan summary

Twenty initiatives selected from the 34-row actionable backlog (post PR #808 intake). Selection covers all 8 High-priority (P1) audit findings plus 12 of 19 Medium (P2) findings, prioritized by source-repo coverage (3× self-audit + 9× sibling-repo cross-section).

## Bundle considerations (Rule 1)

Two pairs flagged as bundle candidates — the deepener will verify or split:

- **Initiatives 10 + 11** — `member-hierarchy-overrides-mislabels-sibling-interface-impls` (gh #736) and `find-overrides-vs-member-hierarchy-cross-tool-inconsistency` (gh #737) both touch `MemberHierarchyService.cs` / `OverridesService.cs` and describe the same cross-tool semantic question (what counts as "override" vs "sibling-interface implementation"). Strong bundle signal.
- **Initiatives 19 + 20** — `analyze-dependencies-prompt-payload-overflow` (gh #755) and `review-test-coverage-prompt-payload-overflow` (gh #756) are payload-cap overflows on different prompts; both likely need the same `PromptMessageBuilder.SerializeTruncatedList` pattern (precedent: PR #790 for guided_extract_interface). Likely bundle.

## Initiatives

### 1. extract-method-preview-same-block-scope-false-negative

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-method-preview-same-block-scope-false-negative` |
| Source | gh #744 (P1 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`extract-method-preview-same-block-scope-false-negative`]. |

### 2. surface-test-teardown-directory-survives-windows-lock

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `surface-test-teardown-directory-survives-windows-lock` |
| Source | gh #745 (P1 — `networkdocumentation` audit; operational risk) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`surface-test-teardown-directory-survives-windows-lock`]. |

### 3. symbol-relationships-builtin-type-unbounded-enumeration

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-relationships-builtin-type-unbounded-enumeration` |
| Source | gh #757 (P1 — `tradewise` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`symbol-relationships-builtin-type-unbounded-enumeration`]. |

### 4. get-coupling-metrics-no-summary-mode

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `get-coupling-metrics-no-summary-mode` |
| Source | gh #763 (P1 — `firewallanalyzer` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`get-coupling-metrics-no-summary-mode`]. |

### 5. validate-workspace-runtests-total-zero

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `validate-workspace-runtests-total-zero` |
| Source | gh #764 (P1 — `firewallanalyzer` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`validate-workspace-runtests-total-zero`]. |

### 6. extract-interface-cross-project-uncompilable

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-interface-cross-project-uncompilable` |
| Source | gh #765 (P1 — `firewallanalyzer` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`extract-interface-cross-project-uncompilable`]. |

### 7. split-service-with-di-broken-output

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `split-service-with-di-broken-output` |
| Source | gh #766 (P1 — `firewallanalyzer` audit; refactor tool emits non-functional code) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`split-service-with-di-broken-output`]. |

### 8. preview-token-stale-across-auto-reload

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `preview-token-stale-across-auto-reload` |
| Source | gh #767 (P1 — `firewallanalyzer` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`preview-token-stale-across-auto-reload`]. |

### 9. set-editorconfig-option-duplicate-key-append

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `set-editorconfig-option-duplicate-key-append` |
| Source | gh #735 (P2 — `roslyn-backed-mcp` self-audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`set-editorconfig-option-duplicate-key-append`]. |

### 10. member-hierarchy-overrides-mislabels-sibling-interface-impls

**Bundle candidate with initiative 11.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `member-hierarchy-overrides-mislabels-sibling-interface-impls` |
| Source | gh #736 (P2 — `roslyn-backed-mcp` self-audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`member-hierarchy-overrides-mislabels-sibling-interface-impls`]. |

### 11. find-overrides-vs-member-hierarchy-cross-tool-inconsistency

**Bundle candidate with initiative 10.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-overrides-vs-member-hierarchy-cross-tool-inconsistency` |
| Source | gh #737 (P2 — `roslyn-backed-mcp` self-audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`find-overrides-vs-member-hierarchy-cross-tool-inconsistency`]. |

### 12. project-diagnostics-totaldiagnostics-collapses-under-severity-filter

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `project-diagnostics-totaldiagnostics-collapses-under-severity-filter` |
| Source | gh #746 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`project-diagnostics-totaldiagnostics-collapses-under-severity-filter`]. |

### 13. symbol-signature-help-returns-bare-null-for-resolvable-method-metadata

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-signature-help-returns-bare-null-for-resolvable-method-metadata` |
| Source | gh #747 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`symbol-signature-help-returns-bare-null-for-resolvable-method-metadata`]. |

### 14. extract-interface-preview-duplicate-interface-when-already-implements

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `extract-interface-preview-duplicate-interface-when-already-implements` |
| Source | gh #748 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`extract-interface-preview-duplicate-interface-when-already-implements`]. |

### 15. change-type-namespace-preview-omits-consumer-using-additions

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `change-type-namespace-preview-omits-consumer-using-additions` |
| Source | gh #749 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`change-type-namespace-preview-omits-consumer-using-additions`]. |

### 16. symbol-refactor-preview-empty-appliedfiles-on-success

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `symbol-refactor-preview-empty-appliedfiles-on-success` |
| Source | gh #750 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`symbol-refactor-preview-empty-appliedfiles-on-success`]. |

### 17. test-run-fqdn-drift-vs-test-discover

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `test-run-fqdn-drift-vs-test-discover` |
| Source | gh #752 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`test-run-fqdn-drift-vs-test-discover`]. |

### 18. find-overrides-payload-overflow-on-corlib-virtual

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `find-overrides-payload-overflow-on-corlib-virtual` |
| Source | gh #754 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`find-overrides-payload-overflow-on-corlib-virtual`]. |

### 19. analyze-dependencies-prompt-payload-overflow

**Bundle candidate with initiative 20.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `analyze-dependencies-prompt-payload-overflow` |
| Source | gh #755 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`analyze-dependencies-prompt-payload-overflow`]. |

### 20. review-test-coverage-prompt-payload-overflow

**Bundle candidate with initiative 19.** Deepener should verify Rule 1 four-conditions test.

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `review-test-coverage-prompt-payload-overflow` |
| Source | gh #756 (P2 — `networkdocumentation` audit) |
| Diagnosis | _Pending deepener._ |
| Approach | _Pending deepener._ |
| Scope | _Pending deepener._ |
| Tool policy | _Pending deepener._ |
| Estimated context cost | _Pending deepener._ |
| Risks | _Pending deepener._ |
| Validation | _Pending deepener._ |
| Performance review | _Pending deepener._ |
| CHANGELOG category | _Pending deepener._ |
| CHANGELOG entry (draft) | _Pending deepener._ |
| Backlog sync | Close rows: [`review-test-coverage-prompt-payload-overflow`]. |
