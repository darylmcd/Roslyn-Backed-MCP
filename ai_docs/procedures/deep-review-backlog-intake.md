# Deep-review backlog intake

<!-- purpose: Backlog reconciliation for multi-repo campaigns. -->
<!-- scope: reference -->

> **SCOPE: reference — context only, assigns no work.** Does not answer "next step?". Route via [`../planning_index.md`](../planning_index.md).

## Default (automated)

Running **`eng/new-deep-review-batch.ps1`** from the Roslyn-Backed-MCP repo root **without** `-SkipBacklogSync`:

1. **Discovers** `*_mcp-server-audit.md` under sibling repositories (parent of this repo, usually `..\` from repo root) plus audits already in `ai_docs/audit-reports/` here.
2. Keeps the **latest timestamp per repo-id** (filename `yyyyMMddTHHmmssZ_<repo-id>_mcp-server-audit.md`).
3. **Imports** into `ai_docs/audit-reports/` (overwrites by default; use `-NoOverwrite` to fail if a file already exists).
4. **Scaffolds** a rollup under `ai_docs/reports/<timestamp>_deep-review-rollup.md`.
5. **Merges** new issue candidates into `ai_docs/backlog.md` (dedupe, P2→P4, sort P4 by `id`, `updated_at`), using conservative rules in `eng/sync-deep-review-backlog.ps1`.

## Optional switches

| Switch | Use |
|--------|-----|
| `-AuditFiles @(...)` | Skip auto-discovery; use an explicit list of audit paths (absolute or relative). |
| `-SiblingRepoParent` | Override the parent folder scanned for siblings (default: parent of the Roslyn-Backed-MCP root). |
| `-ExcludeRepoFolders` | Extra folder names to skip under the parent (default: this repo’s folder name only). |
| `-SkipBacklogSync` | Import + rollup only; do not modify `backlog.md`. |
| `-BacklogWhatIf` | Show what would be added to the backlog without writing. |
| `-NoOverwrite` | Import fails if the canonical file already exists (rare; prefer default overwrite for refresh). |

## Manual follow-up

Fill **Repo matrix**, **Client coverage**, and **Deduped issues** in the new rollup scaffold if you need narrative beyond what automation provides.

## Related

- `deep-review-program.md` — program context
- `deep-review-command-reference.md` — one-liner examples
