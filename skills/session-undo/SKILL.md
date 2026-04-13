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

## Limits

- **`revert_last_apply`** is **one step** — not arbitrary history.
- For multi-step rollback use **git** or manual edits.
- After reload or revert, run **`compile_check`** or **`build_workspace`**.
