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

## Limits

- **`revert_last_apply`** is **one step** — not arbitrary history.
- For multi-step rollback use **git** or manual edits.
- After reload or revert, run **`compile_check`** or **`build_workspace`**.
