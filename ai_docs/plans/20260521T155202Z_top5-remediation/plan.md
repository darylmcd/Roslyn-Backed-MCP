# Top 5 Backlog Remediation Plan

<!-- scope: in-repo -->

Created: 2026-05-21T15:52:02Z

## Selection

Source: `ai_docs/backlog.md` as of `updated_at: 2026-05-21T05:23:00Z`.

Selection rule: take the highest-priority planner-ready rows with concrete remediation/implementation deliverables. Skip rows whose current deliverable is explicitly design-only rather than a fix implementation.

Skipped by rule:

- `workspace-fork-apply-primitive` — High priority, but the current backlog deliverable is a design note plus a follow-on implementation row, not a fix implementation.

Selected remediation rows:

1. `test-run-bare-exception-envelope`
2. `agent-workflow-router-first-hop`
3. `discover-capabilities-quick-path-steering`
4. `test-related-files-direct-reference-ranking`
5. `mcp-resource-server-name-aliasing`

## Plan

1. Harden test execution error envelopes.
   - Wrap `test_run` failures inside the tool body so runner exceptions return the same structured JSON error envelope as other tools.
   - Confirm `test_coverage` has equivalent in-handler protection for pre-run status/partition failures, not only inner runner failures.
   - Add regression coverage for structured `error/category/exceptionType/schemaHint` output on representative failures.

2. Add first-hop workflow routing.
   - Add a compact `recommend_workflow` tool that maps task intent to `primaryTools`, `followUpTools`, `avoid`, `why`, and `requiredWorkspaceState`.
   - Back the router with a small deterministic rule set aligned with `bootstrap-read-tool-primer.md`.
   - Add catalog parity and representative routing tests.

3. Refresh `discover_capabilities` fast-path guidance.
   - Prefer `compile_check`, `validate_recent_git_changes` / `validate_workspace`, `test_related_files`, `document_symbols`, and `find_references` in the prompt and workflow hints.
   - Remove stale routine-default wording that steers normal code edits to `build_workspace` / `build_project`.
   - Add prompt smoke tests for `navigation`, `refactoring`, `testing`, and `all`.

4. Improve `test_related_files` direct-reference ranking.
   - Add a direct semantic reference pass for symbols declared in changed files.
   - Rank direct-reference matches ahead of type/file-name and namespace-neighbor broadening while preserving trigger-file attribution.
   - Add regression coverage showing direct-reference results surface first and the recommended filter follows that order.

5. Stabilize resource server-name guidance.
   - Add a small canonical/alias helper exposed through `server_info` so clients and skills have one place to read the intended resource server names.
   - Update the surface-test prompt Phase 9 to probe live `server_info` / available server names before composing resource URIs, and to avoid underscore-converted plugin names.
   - Add tests for the alias metadata and prompt guidance.

6. backlog: sync `ai_docs/backlog.md`.
   - Remove completed selected rows after implementation and validation.
   - Leave design-only and deferred rows untouched.
