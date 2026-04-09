# Next work and backlog

<!-- purpose: Open work only; contract for agents syncing backlog on ship. -->

**updated_at:** 2026-04-09T20:00:00Z

## Agent contract

**Scope:** This file lists **unfinished work only**. It is not a changelog.

| | |
|---|---|
| **MUST** | Remove or update backlog rows when work ships; do it in the **same PR** as the implementation (or an immediate follow-up). |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md` when closing backlog-related items. |
| **MUST** | Use stable, kebab-case `id` values per open row (grep-friendly). |
| **MUST NOT** | Add "Completed" tables, dated history sections, or narrative changelogs here. |
| **MUST NOT** | Leave done items in the open table. |

## Standing rules (ongoing)

These are **not** backlog rows; do not delete them when clearing completed work.

- Reprioritize on each audit pass.
- Bug and tool items: put reproduction and session context in the PR or linked issue when it helps reviewers.
- Broader planning and complexity notes stay in this file; `ai_docs/archive/` holds policy only (see `archive/README.md`).

## Open work

Ordered by severity: P2 (contract violations / real-world blockers) → P3 (refactors, accuracy, UX) → P4 (docs + cosmetic).

| id | blocker | deps | do |
|----|---------|------|-----|
| `dr-code-fix-preview-vs-diagnostic-details-curated-gap` | none | — | **P3 / product.** **Auto (deep-review).** `20260409T164306Z_itchatbot_mcp-server-audit.md` §14.1 (CA1852 / empty `SupportedFixes`); `20260409T165739Z_roslyn-backed-mcp_mcp-server-audit.md` §7 (`diagnostic_details` lists `remove_unused_field` for CS0414 but `code_fix_preview` rejects curated fix). Unify contract: which diagnostics have curated fixes vs FixAll vs IDE-only. |
| `dr-semantic-search-idisposable-predicate-accuracy` | none | — | **P3 / product.** **Auto (deep-review).** `20260409T164306Z_itchatbot_mcp-server-audit.md` §14.5 — **`semantic_search` “classes implementing IDisposable”** returns types that are not implementors (e.g. test fixtures). **FLAG** predicate accuracy. |
| `dr-find-shared-members-locator-invalidargument` | none | — | **P3 / product.** **Auto (deep-review).** `20260409T164306Z_itchatbot_mcp-server-audit.md` §14.3 — **`find_shared_members`** → **InvalidArgument** “Syntax node is not within syntax tree” for `ChatOrchestrationPipeline` via handle/locator (partial-class / snapshot mismatch hypothesis). |
| `dr-security-integration-insecurelib-not-in-sln` | none | — | **P3 / test graph.** **Auto (deep-review).** `20260409T165739Z_roslyn-backed-mcp_mcp-server-audit.md` §14.1 — Full `dotnet test` on `Roslyn-Backed-MCP.sln`: **4 failures** in `SecurityDiagnosticIntegrationTests` (no findings in `InsecureLib`). Loaded solution lacks insecure fixture projects tests expect — fix test preconditions, sample layout, or conditional skip. |
| `dr-project-diagnostics-info-only-empty-first-page` | none | — | **P4 / tracking.** **Auto (deep-review).** Merged: `20260409T165230Z_firewallanalyzer_mcp-server-audit.md` §14.1 (null `severity`, non-zero info totals, empty page) + `20260409T165300Z_networkdocumentation_mcp-server-audit.md` §14.2 (first page empty without explicit `severity` when only Info diagnostics exist). **`project_diagnostics` paging / default severity** UX — **FLAG**. |
| `dr-get-editorconfig-options-incomplete-after-set` | none | — | **P4 / tracking.** **Auto (deep-review).** `20260409T164306Z_itchatbot_mcp-server-audit.md` §14.4 — After `set_editorconfig_option`, **`get_editorconfig_options`** omitted `dotnet_separate_import_directive_groups` from `Options[]` while disk had the line. **FLAG** enumerator completeness vs Roslyn API. |
| `dr-get-code-actions-opaque-error-on-bad-contract` | none | — | **P4 / UX.** **Auto (deep-review).** `20260409T165300Z_networkdocumentation_mcp-server-audit.md` §14.1 — **`get_code_actions`** called with `line`/`column` instead of `startLine`/`startColumn`: generic “error occurred invoking” without required-property hint — error-message-quality / schema discoverability. |
| `dr-project-diagnostics-response-total-field-naming` | none | — | **P4 / docs.** **Auto (deep-review).** `20260409T165739Z_roslyn-backed-mcp_mcp-server-audit.md` §7 — **`project_diagnostics`** uses `totalErrors` / `compilerErrors` split vs docs/examples expecting uniform `total*` naming — **FLAG** documentation / contract clarity (not necessarily wrong behavior). |
| `dr-find-references-bulk-first-invocation-parameter-ux` | none | — | **P4 / UX.** **Auto (deep-review).** `20260408T195030Z_firewall-analyzer_mcp-server-audit.md` §14 — **`find_references_bulk`** first invocation failed until parameter shape corrected; improve schema example in error (cosmetic). |

## Refs

| Path | Role |
|------|------|
| `ai_docs/workflow.md` | Branch/PR workflow; **Backlog closure** |
| `ai_docs/backlog.md` | This file (open work + contract) |
