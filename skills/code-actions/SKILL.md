---
name: code-actions
description: "Roslyn code actions (fixes and refactorings). Use when: applying IDE-style quick fixes or refactorings at a position or selection, including introduce parameter and inline temporary variable. Takes file path and line/column (and optional selection end) as input."
user-invocable: true
argument-hint: "file path and line:column [endLine:endColumn]"
---

# Roslyn Code Actions

You help apply **Roslyn code actions** exposed by the MCP host: list actions at a span, preview the diff, apply with the preview token, then validate.

## Input

From `$ARGUMENTS` (or the user's message), obtain:

- Absolute **`filePath`**
- **`startLine`**, **`startColumn`** (1-based)
- Optionally **`endLine`**, **`endColumn`** for **selection-range** refactorings (e.g. introduce parameter, inline temporary)

If a workspace is not loaded, ask for the solution path and call `workspace_load` first.

## Server discovery

Use **`server_info`**, **`roslyn://server/catalog`**, MCP prompt **`discover_capabilities`** (`refactoring`), or **`refactor_and_validate`** when you already know the span and want a templated workflow message.

## Safety rules

1. **Preview before apply:** `preview_code_action` → review diff → `apply_code_action`.
2. **Verify after apply:** `compile_check` or `build_project` / `build_workspace`.
3. **One change at a time** unless the user explicitly asked for a batch.

## Workflow

1. Call **`get_code_actions`** with `workspaceId`, `filePath`, `startLine`, `startColumn`, and end coordinates if the user highlighted a range.
2. Present the numbered actions. Ask which index to run if unclear.
3. Call **`preview_code_action`** with that index. Show the diff summary.
4. After confirmation, call **`apply_code_action`** with the preview token.
5. Run **`compile_check`** (fast) or **`build_workspace`**.
6. If the result is wrong, consider **`revert_last_apply`** then retry with a different action or span.

For diagnostics-first flows, combine with **`diagnostic_details`**, **`code_fix_preview`** / **`code_fix_apply`**, or **`fix_all_preview`** when fixing every instance of one ID.
