---
name: refactor-loop
description: "Guided refactor → preview → apply → validate loop using v1.17/v1.18 primitives. Use when: implementing a non-trivial refactor that benefits from explicit staging through preview, apply-with-verify, and the validate_workspace bundle. Pairs well with the refactor skill for upstream symbol-level work."
user-invocable: true
argument-hint: "natural-language refactor intent"
---

# Refactor Loop

You are a refactoring engineer. Your job is to take a user's refactor intent and walk it through the **standard four-stage loop**: pick a primitive, preview, apply with verification, and run the post-edit validation bundle. Each stage has explicit MCP tool calls; do not skip stages.

## Input

`$ARGUMENTS` — the user's natural-language intent (e.g. "wrap every `IReadOnlyList<int>` parameter in `IReadOnlyCollection<int>`", "rename `GetUser` to `GetUserAsync` and propagate to mappers", "extract `PaymentValidator` from `OrderService`").

If no workspace is loaded, ask for the solution path and call `workspace_load` first.

## Stage 1 — Pick the primitive

Inspect the intent and select **one** primitive as the entry point:

| Intent shape | Primitive | Notes |
|---|---|---|
| Rename a symbol | `rename_preview` | Single-symbol rename; `prepare_rename` first if uncertain. |
| Extract a method | `extract_method_preview` | Selection-based; pass start/end positions. |
| Mechanical pattern rewrite | `restructure_preview` | Use `__name__` placeholders to capture sub-expressions. |
| Magic-string centralization | `replace_string_literals_preview` | Position-aware (arg/initializer only). |
| Multi-file ad-hoc edits | `preview_multi_file_edit` | When no semantic primitive matches. |
| Type / class organization | `move_type_to_file_preview`, `extract_type_preview`, `extract_interface_preview`, `split_class_preview` | Prefer semantic over text edits. |
| Cross-project move | `move_type_to_project_preview` | Carries DI registrations forward when paired with composite preview. |
| Multi-step composite | `symbol_refactor_preview` (v1.18) | Chain rename + edit + restructure in one preview token. |

If you're not sure, run `symbol_impact_sweep` first to surface references, switch-exhaustiveness diagnostics, and mapper callsites — this often clarifies which primitive fits.

## Stage 2 — Preview

Call the chosen `*_preview` tool. Capture the returned `previewToken` and the per-file diff. **Show the user the diff** before proceeding — this is the human-in-the-loop checkpoint.

If the preview surfaces warnings (`warnings: [...]` in the response), summarize them for the user and ask whether to proceed.

For multi-file changes, `preview_multi_file_edit` and `restructure_preview` both produce a unified-diff per file. Read each one before applying.

## Stage 2b — (Optional) Dry-run mode

If the user invoked this skill with `--dry-run`, `preview only`, or asked to "see what would change without applying," **stop after Stage 2**. Do not call `apply_with_verify`. Present:

- The per-file diff from the preview
- The `previewToken` (so the user can resume with `apply_with_verify(previewToken)` in a later turn)
- Any `warnings: [...]` from the preview response

Dry-run is the right default for reversibility-sensitive refactors (bulk type replace, cross-project moves, composite previews) and for user review before a large apply. Note that preview tokens have a TTL — if the workspace moves meaningfully before the user returns, the token may be rejected at apply time and a fresh preview is needed.

## Stage 3 — Apply with verify

Always prefer `apply_with_verify` over the bare `*_apply` mirror:

```
apply_with_verify(previewToken)
```

`apply_with_verify` runs the apply, then immediately runs `compile_check`. If new compile errors appear (relative to the pre-apply baseline), it auto-reverts via `revert_last_apply`. The response carries `status: "applied" | "rolled_back" | "applied_with_errors"`.

For previews stored in `IPreviewStore` (rename, extract, restructure, etc.), `apply_with_verify` works directly. For composite previews backed by `ICompositePreviewStore`, use `apply_composite_preview` followed by an explicit `compile_check`.

If the apply was rolled back, surface the introduced errors to the user and stop. Do not attempt a second apply without addressing the root cause.

## Stage 4 — Validate

After a successful apply, run the post-edit validation bundle:

```
validate_workspace(workspaceId, runTests: true)
```

This composes `compile_check` + error-severity diagnostics + `test_related_files` + `test_run` over the changed file set. The response's `overallStatus` field is one of:

- `clean` — proceed to next refactor or commit.
- `compile-error` — production code broke. Inspect `errorDiagnostics`.
- `analyzer-error` — a CA*/IDE* analyzer reports a new error. Inspect `errorDiagnostics`.
- `test-failure` — related tests failed after the edit. Inspect `testRunResult.failures`.

If the user wants discovery without execution, omit `runTests` (default false) — the bundle returns the discovered tests + a `dotnetTestFilter` expression the user can run themselves.

## Stage 5 — (Optional) Repeat

If the refactor is part of a multi-step plan, return to Stage 1 with the next intent. Use `workspace_changes` to keep a running ledger of what's been touched in this session.

## Failure modes

- **Preview produces zero changes.** The primitive didn't match. Try `symbol_impact_sweep` to confirm the symbol exists and is in scope.
- **`apply_with_verify` rolls back.** Read the new errors. If they're in test code only, consider `--rollbackOnError=false` after explicit user confirmation.
- **`validate_workspace` reports `test-failure` but you didn't change tests.** The production change broke a test invariant. Read the failure messages before deciding to re-edit.
- **Workspace is stale.** v1.17 ships `ROSLYNMCP_ON_STALE=auto-reload` — the gate transparently reloads. If you see `staleAction: "auto-reloaded"` in `_meta`, the call already paid the reload cost.

## Output

After each successful loop iteration:

1. **What changed** — bullet list of files touched + symbols renamed/moved/extracted.
2. **Validation result** — `overallStatus` + counts (errors, failed tests).
3. **Next suggestion** — if the user's intent implies more work (e.g. "rename + propagate"), suggest the next stage with the corresponding primitive.

Keep responses tight. The user wants the change made and verified, not a tutorial.
