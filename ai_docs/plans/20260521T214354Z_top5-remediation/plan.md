# Top 5 Backlog Remediation Plan

<!-- scope: in-repo -->

Created: 2026-05-21T21:43:54Z

## Selection

Source: `ai_docs/backlog.md` as of `updated_at: 2026-05-21T16:03:59Z`.

Selection rule: take the highest-priority open rows in backlog order. Design-only rows count when their current deliverable is a concrete design artifact plus a follow-on implementation row.

Selected rows:

1. `workspace-fork-apply-primitive`
2. `symbol-search-nearest-fuzzy-fallback`
3. `surface-test-resumability-cleanup-skill`
4. `plugin-package-files-allowlist`
5. `find-duplicate-helpers-framework-wrapper-filter-leak`

## Plan

1. Write `ai_docs/items/workspace-fork-apply-primitive.md` with the tool-shape decision, lifecycle semantics, failure modes, restart behavior, and one bounded implementation follow-up row.
2. Add closest-match suggestions to metadata-name NotFound envelopes, starting with a structured `closestMatches` array emitted by the existing tool error boundary.
3. Add surface-test crash-recovery support by documenting a `--cleanup-only` mode and a per-phase checkpoint/resume contract.
4. Write `ai_docs/items/plugin-package-files-allowlist.md` with the Claude Code plugin packaging mechanism decision, consumer file set, release-verification shape, and one bounded implementation follow-up row.
5. Extend duplicate-helper framework-wrapper filtering for Serilog hosting, CORS service-registration, and HTTP resilience extension wrappers.
6. Add focused regression coverage for the changed behavior and prompt contracts.
7. backlog: sync `ai_docs/backlog.md`, replacing completed design rows with implementation rows and removing completed remediation rows.
8. changelog: add fragments for every completed row and confirm the existing post-2.2.1 fragment covers the already-merged top-5 batch.
