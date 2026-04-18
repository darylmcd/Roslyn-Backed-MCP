---
name: session-undo
description: "Session history and undo. Use when: the user wants to see what Roslyn MCP changed, undo the last apply, reload the workspace from disk, or recover from a bad refactoring."
user-invocable: true
argument-hint: "[workspaceId if multiple sessions]"
---

# Workspace Session Undo

You manage **session-scoped** history for Roslyn MCP applies. You do **not** replace git.

## Input

Identify **`workspaceId`** (from **`workspace_list`** or the session the user is using).

## Server discovery

MCP prompt **`session_undo`** summarizes this workflow. Full inventory: **`roslyn://server/catalog`**.

## Workflow

1. **`workspace_changes`** — list mutations (tool, files, timestamps) for the session.
2. **`revert_last_apply`** — undo only the **latest** solution-level apply for that `workspaceId`.
3. **`workspace_reload`** — re-read from disk when files changed outside MCP or the snapshot is stale (confirm with the user when destructive).
4. **`workspace_close`** — tear down the session when finished.

## Atomic apply + verify (v1.15+)

**`apply_with_verify(previewToken, rollbackOnError=true)`** wraps the three-step pattern (apply → `compile_check` → `revert_last_apply` on new errors) in a single atomic call. Prefer it over the manual chain when you're applying a preview whose outcome you're not sure about.

- On a clean apply: returns `{status: "applied", appliedFiles: [...]}`.
- On new compile errors with `rollbackOnError=true` (default): returns `{status: "rolled_back", introducedErrors: [...]}` and the workspace is back to the pre-apply state.
- Pass `rollbackOnError=false` to preserve the broken state for manual inspection (returns `{status: "applied_with_errors"}`).

## Diff view — what changed since a previous apply point

When the user asks "what has this session changed?" or "show me the diff since the last apply," produce a structured diff view without committing to an undo:

1. **`workspace_changes`** — pull the session ledger (tool name, files, timestamps, optional preview token if still live).
2. For each file in the ledger, show the current-vs-original state:
   - Prefer the server's own change record (if `workspace_changes` returns pre-/post-apply text snippets or a preview-token-backed diff).
   - Otherwise read the current file via `get_source_text`, then cross-reference with `git diff` if the repo has git (assume the user controls the working tree; do not shell out without asking).
3. Group by apply-event timestamp, with a heading per event showing tool + affected files.
4. At the bottom, offer next actions:
   - `revert_last_apply` to undo the most recent apply
   - `workspace_reload` if on-disk state drifted from the workspace snapshot
   - Manual `git` rollback for multi-step undo

This mode is read-only — don't mutate anything without explicit user direction.

## Limits

- **`revert_last_apply`** is **one step** — not arbitrary history.
- For multi-step rollback use **git** or manual edits.
- After reload or revert, run **`compile_check`** or **`build_workspace`**.
- Diff view relies on what `workspace_changes` records in this session; applies from a previous session are not in scope — use `git` for cross-session history.
