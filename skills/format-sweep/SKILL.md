---
name: format-sweep
description: "Whole-solution formatting compliance pass. Use when: enforcing whole-solution formatting, running `dotnet format` equivalents, bulk-organizing usings, or performing an editorconfig compliance sweep. Optionally takes a project name or file glob to narrow scope."
user-invocable: true
argument-hint: "(optional) project name or file glob"
---

# Format Sweep

You are a C# formatting compliance specialist. Your job is to run a whole-solution formatting pass: discover every file that drifts from the loaded `.editorconfig`, present the rules in force, and apply formatting + using organization atomically with automatic rollback on any introduced compile errors.

## Input

`$ARGUMENTS` is an optional scope filter: a project name (e.g. `MyProject`) or a file glob (e.g. `src/Services/**/*.cs`). If omitted, the sweep runs across the entire loaded workspace.

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (category `formatting` or `all`) for the live tool list and **WorkflowHints** (preview/apply, verify wrappers, editorconfig surface).

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out for formatting.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path (if not already loaded).
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm the workspace loaded cleanly and note any load-time warnings. Abort if status reports failure.

### Step 2: Surface EditorConfig Rules (Transparency)

1. Call `get_editorconfig_options` for the workspace (or narrowed scope from `$ARGUMENTS`).
2. Present a concise summary of the active rules the sweep will enforce: indent style/size, newline preferences, using-ordering/placement, `dotnet_diagnostic.*` severities, and any category-level overrides.
3. This step is informational — surface it so the user sees *why* a file is flagged before any writes occur.

### Step 3: Discover Violations

1. Call `format_check` with the optional scope filter. This is report-only — no edits are made.
2. Collect the returned file list and per-file violation details.
3. If `format_check` reports zero violating files, stop and report success (see **Refusal conditions**).
4. Group findings by file. Summarize: total files scanned, violating files, dominant rule IDs.

### Step 4: Confirm Scope

1. Present the grouped per-file violation list to the user.
2. Warn that the sweep will touch every violating file. Ask for explicit confirmation before proceeding.
3. Remind the user that `apply_with_verify` will auto-rollback any individual file whose apply introduces a new compile error.

### Step 5: Format Files (preview → apply with verify)

For each violating file, in batches:

1. Call `format_document_preview` to generate the edit.
2. Wrap the apply in `apply_with_verify` — the wrapper runs `compile_check` after each apply and auto-reverts the file if new errors appear.
3. For edits targeting a known range only (e.g. a specific region flagged by `format_check`), prefer `format_range_preview` → `format_range_apply` via `apply_with_verify` to minimize churn.
4. Batch where the server accepts multi-file verification; otherwise process sequentially and record any auto-rollbacks.

### Step 6: Organize Usings (preview → apply)

For the same set of files that had formatting drift (and any file whose using block was flagged):

1. Call `organize_usings_preview` per file.
2. Apply via `organize_usings_apply`, again wrapped in `apply_with_verify` for compile-safety.
3. Record files touched and any rollbacks.

### Step 7: Final Verification

1. Call `format_check` again across the same scope — expect zero violating files.
2. Call `validate_workspace` for the overall-status bundle (compile, diagnostics, format). Confirm green.
3. Call `compile_check` once more to confirm the final state builds clean.

### Step 8: Report

Produce the **Output Format** block below.

## Safety rules

1. **Clean working tree.** This skill is not git-state aware. Before starting, ask the user to confirm the working tree is clean (or that they have committed/stashed in-flight work). Do not proceed without explicit confirmation.
2. **Always apply with verify.** Every `*_apply` call in this skill must be wrapped by `apply_with_verify` so that any file whose edit introduces a new compile error auto-reverts.
3. **Preview before apply.** Never call an `*_apply` tool without calling the corresponding `*_preview` first.
4. **Per-file atomicity.** If a batched apply fails verification on one file, that file must roll back without reverting the rest of the batch.
5. **No scope creep.** This skill formats and organizes usings only. Do not invoke rename, extract, or other transforms even if the preview surfaces them as options.
6. **Large sweeps warn.** If the violation count exceeds 50 files, surface the count and re-confirm with the user before the apply phase.

## Output Format

Present a structured report:

```
## Format Sweep Report: {workspace-name}{optional-scope}

### Summary
- Files scanned: {count}
- Violations before: {count} files / {count} rule hits
- Violations after: {count} files (expect 0)
- Files touched: {count}
- Rollbacks (auto-reverted by verify): {count}
- Final `format_check` status: {pass/fail}
- `validate_workspace` status: {pass/fail}

### EditorConfig rules enforced
{condensed list: indent, newline, using-ordering, key dotnet_diagnostic severities}

### Files touched
{table: file path, rule IDs resolved, formatted (y/n), usings organized (y/n), rolled back (y/n)}

### Rollbacks (if any)
{per-file: file, rule attempted, compile error returned, action suggested}

### Next steps
{if any rollbacks: suggested follow-up (manual fix, narrower range, skill handoff). Otherwise: note that the sweep is clean and recommend committing.}
```

## Refusal conditions

Stop and report (without writing) when any of these hold:

1. **Workspace load failed.** `workspace_load` or `workspace_status` reports failure — bail with the load-time error and a suggested reload path.
2. **Zero violations.** `format_check` in Step 3 returns no files. Report success: "Solution is clean against the loaded `.editorconfig` — no sweep needed." Do not proceed to apply.
3. **Connectivity.** `server_info` precheck fails (see **Connectivity precheck** above).
4. **User declines confirmation** at Step 4 — exit without edits.
