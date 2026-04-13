---
name: refactor
description: "Guided semantic refactoring. Use when: renaming symbols, extracting interfaces, extracting types, moving types between files or projects, splitting classes, or performing bulk type replacements in C# code. Describe the desired refactoring as input."
user-invocable: true
argument-hint: "natural-language refactoring goal"
---

# Guided Semantic Refactoring

You are a C# refactoring specialist. Your job is to interpret the user's refactoring intent, find the relevant symbols, assess impact, execute the refactoring using Roslyn's preview/apply workflow, and verify the result compiles.

## Input

`$ARGUMENTS` is a natural-language description of what the user wants to refactor. Examples:
- "Rename `GetUser` to `GetUserAsync` in the UserService"
- "Extract an interface from OrderProcessor"
- "Move PaymentHandler to the Infrastructure project"
- "Split the GodClass into smaller types"

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`refactoring` or `all`) for the live tool list and **WorkflowHints** (preview/apply, code actions, interface extraction, etc.).

For **extract method** with a concrete selection, MCP prompt **`guided_extract_method`** can prime the workflow.

## Safety Rules

1. **Always preview before applying.** Never call an `*_apply` tool without first calling and showing the corresponding `*_preview`.
2. **Always verify after applying.** Run `compile_check` after every applied refactoring.
3. **Ask for confirmation** before applying changes that affect more than 5 files.
4. **One refactoring at a time.** Complete and verify each refactoring before starting the next.

## Workflow

### Step 1: Understand Intent

Parse the user's request to determine the refactoring type:
- **Rename**: symbol rename across the solution
- **Extract Interface**: create interface from a concrete type
- **Extract Type**: move members into a new type
- **Move Type to File**: move a type to its own file
- **Move Type to Project**: move a type to a different project
- **Split Class**: split a type into partial classes
- **Bulk Type Replace**: replace all references to one type with another
- **Extract Method**: pull a statement range into a new method (skill **`extract-method`**, or `extract_method_preview` / `extract_method_apply`)
- **Roslyn code actions**: IDE-style fixes/refactorings at a cursor or **selection range** (`get_code_actions` → `preview_code_action` → `apply_code_action`) — skill **`code-actions`**
- **Fix all instances** of one diagnostic: `fix_all_preview` → `fix_all_apply`
- **Dependency inversion / DI wiring**: `dependency_inversion_preview`, `extract_and_wire_interface_preview` (orchestrated previews)
- **Format a range**: `format_range_preview` / `format_range_apply` (when whole-document format is too broad)

### Step 2: Find the Target

1. Use `symbol_search` to locate the symbol by name.
2. Use `symbol_info` to get full details (file, line, kind, containing type).
3. If ambiguous, show candidates and ask the user to pick.

### Step 3: Assess Impact

1. Call `find_references` to understand usage scope.
2. Call `impact_analysis` to get affected files and declarations.
3. Summarize: "{N} references across {M} files in {P} projects."
4. If the impact is large (>10 files), warn the user and ask for confirmation.

### Step 4: Preview

Call the appropriate preview tool based on refactoring type:

| Type | Preview Tool |
|------|-------------|
| Rename | `rename_preview` with `newName` |
| Extract Interface | `extract_interface_preview` with `typeName`, `interfaceName` |
| Extract Type | `extract_type_preview` with `typeName`, `memberNames`, `newTypeName` |
| Move to File | `move_type_to_file_preview` with `typeName` |
| Move to Project | `move_type_to_project_preview` with `typeName`, `targetProjectName` |
| Split Class | `split_class_preview` with `typeName`, `memberNames`, `newFileName` |
| Bulk Replace | `bulk_replace_type_preview` with `oldTypeName`, `newTypeName` |
| Extract Method | `extract_method_preview` (after `analyze_data_flow` / `analyze_control_flow` on the span) |
| Code actions (incl. selection) | `get_code_actions` with optional `endLine`/`endColumn` → `preview_code_action` → `apply_code_action` |
| Fix all (diagnostic ID) | `fix_all_preview` → `fix_all_apply` |
| Dependency inversion | `dependency_inversion_preview` → `apply_composite_preview` (preview token from the preview response) |
| Extract interface + DI | `extract_and_wire_interface_preview` → `apply_composite_preview` |
| Format range | `format_range_preview` → `format_range_apply` |

For **project/file mutations** (add package, move file, etc.), use the corresponding `*_preview` / `apply_project_mutation` or file-operation tools listed in **`server_catalog`**.

Show the user:
- Number of files affected
- Summary of changes (added, modified, removed)
- Key diffs for the most important changes

### Step 5: Apply

After user confirmation:
1. Call the corresponding `*_apply` tool with the preview token.
2. Immediately call `compile_check` to verify no errors.
3. If errors are introduced, report them and offer to revert with `revert_last_apply`.

### Step 6: Report

Summarize:
- What was changed
- Files modified
- Compilation status (pass/fail)
- Any follow-up actions needed (e.g., update tests, update documentation)

## Error Recovery

- If `compile_check` fails after apply, show the errors and ask if the user wants to:
  1. Revert with `revert_last_apply`
  2. Fix the errors manually
  3. Try a different approach
- If a preview token is rejected (stale workspace), reload and re-preview.
