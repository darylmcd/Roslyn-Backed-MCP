---
name: refactor
installed_as: roslyn-mcp:refactor
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

**Workspace auto-probe (run before anything else):**

1. **Glob CWD (depth=1)** for `*.slnx`, `*.sln`, and `*.csproj`.
   - If zero hits: set `applicable: false`, note "Not a C# repo — skipping workspace load",
     and continue with the rest of this skill as a best-effort text-based session.
2. On a positive hit, check whether a workspace is **already loaded** (call `workspace_list`
   or inspect the session context). If already loaded, do NOT reload.
3. If not yet loaded, call `workspace_load` with the found path.
   Prefer `.slnx` > `.sln` > bare `.csproj` when multiple exist.
4. Surface the following **recommended-tool block** for this session:
   - `find_references` — find all callers of a symbol
   - `find_consumers` — find all callers of a type/member across projects
   - `find_implementations` — find concrete implementations of an interface/abstract member
   - `compile_check` — verify the solution compiles after each meaningful edit
   - `validate_workspace` — post-mutation gate: compile + diagnostics + related tests

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

### Step 2: Find the Target and capture its handle

1. Use `symbol_search` to locate the symbol by name. Each result includes a `symbolHandle` — **capture it from the chosen match** and keep it for every downstream step.
2. Use `symbol_info` with `symbolHandle:` (not file/line) to confirm full details.
3. If ambiguous, show candidates and ask the user to pick. Keep only the chosen handle — discard the rest.
4. **Propagate `symbolHandle` to every downstream tool** (`find_references`, `impact_analysis`, `rename_preview`, etc.) instead of re-passing file/line. Handles disambiguate overloads, partial classes, and tuple-deconstruction lines where coordinate lookups can drift to an adjacent symbol on busy lines.

### Step 3: Assess Impact

1. Call `find_references` with the captured `symbolHandle`.
2. Call `impact_analysis` with the same `symbolHandle`.
3. Summarize: "{N} references across {M} files in {P} projects."
4. If the impact is large (>10 files), warn the user and ask for confirmation.

### Step 4: Preview

Call the appropriate preview tool based on refactoring type:

| Type | Preview Tool |
|------|-------------|
| Rename | `rename_preview` with `symbolHandle` + `newName` (prefer handle over file/line per the tool's docstring) |
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
4. **Handle rotation**: after a rename / signature-change / type-move, the captured `symbolHandle` from Step 2 points at the *pre-mutation* symbol and may no longer resolve in the current solution. Re-resolve via `symbol_search` (with the new name) or `symbol_info` (by file/line of an applied file) before issuing follow-up calls on the same symbol.

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

## Step 7: Session Self-Check

After completing all refactoring work, emit a final summary:

```
summary: { semanticCalls: N, classificationApplied: <repo-stack> }
```

Determine `classificationApplied`:
- Glob CWD (depth=1) for `*.slnx`, `*.sln`, `*.csproj` → if any found: `"csharp"`
- Otherwise: `"unknown"`

Determine `semanticCalls`: count of Roslyn semantic tool calls made during this session (e.g. `find_references`, `symbol_search`, `document_symbols`, `rename_preview`, `extract_method_preview`, `compile_check`, etc.). Do NOT count `workspace_load`, `workspace_list`, `workspace_status`, or `server_info` — those are infrastructure calls, not semantic calls.

If `classificationApplied == "csharp"` AND `semanticCalls == 0`:
> **Warning:** This session operated on a C# repository but made zero Roslyn semantic tool calls. The refactoring may have relied on text-based editing rather than semantic analysis. Consider re-running with the Roslyn `workspace_load` tool + semantic tools for correctness guarantees. Your client assigns the tool prefix from the registration path it loaded the server under, so that tool may surface as `mcp__roslyn__workspace_load`, as `mcp__plugin_roslyn-mcp_roslyn__workspace_load`, or under another prefix entirely — those are **examples, not an allowed list**; use whichever prefix your own tool surface exposes.
