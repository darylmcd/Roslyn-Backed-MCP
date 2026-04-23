# Tool Usage Guide for AI Agents

<!-- purpose: Help agents pick Roslyn MCP tools and preview/apply workflows. -->

This document helps AI agents choose the right tools and workflows for common tasks.
Call `discover_capabilities` with a category to get contextual guidance, or use `server_info` for a full capability overview.

**Policy:** Use the Roslyn MCP server for C# **refactoring** as well as discovery—see [`runtime.md`](../runtime.md) (*Roslyn MCP client policy*).

## Quick Decision Tree

**"I need to understand code"** → Start with `symbol_search`, `symbol_info`, `go_to_definition`, `document_symbols`

**"I need to find who uses X"** → `find_references` (single symbol), `find_references_bulk` (multiple), `find_consumers` (type-level dependency analysis)

**"I need to assess change impact"** → `impact_analysis` (reference-level), `find_consumers` (type-level with dependency kinds)

**"I need to check code quality"** → `get_complexity_metrics` (method complexity), `get_cohesion_metrics` (type cohesion/SRP), `find_unused_symbols` (dead code)

**"I need to refactor a type"** → See [SRP Refactoring Workflow](#srp-refactoring-workflow) below

**"I need to fix errors"** → `project_diagnostics` → `diagnostic_details` → `code_fix_preview` → `code_fix_apply`

**"I need to build/test"** → `build_workspace` (compile), `test_run` (run tests), `test_related_files` (targeted tests after changes)

**"I need to verify after a multi-file edit"** → `validate_recent_git_changes` (auto-scoped to `git status`), or `validate_workspace` with an explicit `changedFilePaths` — see [Verification workflow](#verification-workflow-post-edit-default)

## Parallel read fan-out

If you need several independent answers from the same loaded workspace, issue
read-only calls in parallel when the client supports concurrent MCP requests.
Good pairings include multiple `find_references` / `symbol_search` lookups, or
navigation plus `compile_check`.

- Safe: read-only analysis/navigation/verification on an already-loaded
  workspace.
- Unsafe to overlap: `workspace_load`, `workspace_reload`, `workspace_close`,
  and all `*_apply` / `apply_*` mutation flows.
- If the host serializes requests, fall back to sequential calls and note that
  the client blocked parallelism rather than the server.

## Verification workflow (post-edit default)

After any C# edit — `Edit`, `Write`, or `*_apply` — the canonical verify for
**multi-file semantic edits** is the `validate_workspace` bundle, scoped to the
touched-file set. Prefer the auto-scoped companion `validate_recent_git_changes`
when you have uncommitted edits in a git working tree; it derives the touched-file
set from `git status --porcelain` so you don't need to enumerate paths by hand.

**Single call, scoped verify (preferred):**

```text
validate_recent_git_changes(workspaceId)
  → derives changedFilePaths from `git status --porcelain`
  → runs compile_check + project_diagnostics (errors) + test_related_files
  → scoped to the projects that own the touched files
  → returns an aggregate envelope: overallStatus = clean | compile-error | analyzer-error | test-failure
```

Falls back to full-workspace scope with a `Warnings` entry when git is not
available on PATH, the solution directory is not inside a git repository, or
`git status` exits non-zero. In that case callers should trust
`OverallStatus` as usual — the bundle still runs — but know the scope is wider
than the touched-file set.

**Explicit file list (when not in a git repo or for targeted verify):**

```text
validate_workspace(workspaceId, changedFilePaths: ["src/.../Foo.cs", "src/.../Bar.cs"])
```

**Primitive-level verify triple** (when you need one primitive at a time —
e.g. compile-only verify after a trivial single-line edit, or running tests
for an arbitrary filter):

1. `compile_check` — structured diagnostics on the loaded workspace. Sub-second on a
   warm workspace; identical diagnostic coverage to `dotnet build` modulo analyzer
   packages (which are the same set this repo's MSBuild targets pull in).
2. `test_related_files` → `test_run --filter "<filter>"` — derive the test filter from
   the touched-file set, then run only the relevant subset. Returns in seconds instead
   of the minutes a full `dotnet test` takes.
3. `format_check` — confirm `dotnet format`-equivalent whitespace / using-ordering is
   clean on the touched files only.

Example (after editing `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs`):

```text
compile_check(workspaceId, projectFilter: "RoslynMcp.Roslyn")
test_related_files(workspaceId, filePaths: ["src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs"])
  → returns filter "FullyQualifiedName~SymbolSearch"
test_run(workspaceId, filter: "FullyQualifiedName~SymbolSearch")
format_check(workspaceId, filePaths: ["src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs"])
```

**Rule of thumb:** for multi-file edits, reach for `validate_recent_git_changes`
first — one call, scoped to touched files, aggregate pass/fail. Drop to the
primitive triple only when the bundle's shape doesn't fit (e.g. you need
compile-only verify without test discovery, or want to run a custom test filter).

### Shell fallbacks — CI-parity only

Reach for these **only** when you need byte-identical CI parity (e.g., the final
`verify-release.ps1` check before cutting a release), or when the MCP server is
disconnected and the [fallback column in the primer](../bootstrap-read-tool-primer.md#pattern--tool-read-sideawayssafe)
applies:

- `Bash: dotnet build <sln> -c Release -p:TreatWarningsAsErrors=true` — full MSBuild
  cycle, ~5–30s; matches CI exactly.
- `Bash: dotnet test <testproj> -c Release --no-build` — full suite, minutes; matches
  CI exactly.
- `Bash: dotnet format --verify-no-changes` — full-solution formatter check.

Routine post-edit verify should **not** use the shell commands. The MCP triple returns
the same signal 5–30× faster and with structured output the caller can inspect without
re-parsing stdout.

## Tool Categories

### Navigation & Search (read-only)
| Tool | When to Use |
|------|-------------|
| `symbol_search` | Find symbols by name pattern across the workspace |
| `symbol_info` | Get detailed metadata for a symbol at a location |
| `go_to_definition` | Navigate to where a symbol is declared |
| `goto_type_definition` | Navigate to the type of a variable/parameter/field |
| `document_symbols` | Get all declarations in a file as a hierarchical tree |
| `enclosing_symbol` | Find which method/type a cursor position is inside |
| `get_completions` | Get IntelliSense completions at a position |

### Reference Analysis (read-only)
| Tool | When to Use |
|------|-------------|
| `find_references` | Find all usages of a single symbol |
| `find_references_bulk` | Find usages for up to 50 symbols at once |
| `find_implementations` | Find concrete implementations of an interface/abstract member |
| `find_overrides` | Find overrides of a virtual/abstract member |
| `find_base_members` | Find the base members a symbol overrides or implements |
| `member_hierarchy` | Combined view of base + override chain for a member |
| `symbol_relationships` | Combined view of definitions, references, implementations, and overrides |
| `find_consumers` | Find all types depending on a type/interface, classified by dependency kind |
| `find_property_writes` | Find all locations where a property is assigned |
| `find_type_usages` | Find all usages of a type, classified by role (parameter, field, return type, etc.) |

### Quality & Metrics (read-only)
| Tool | When to Use |
|------|-------------|
| `get_complexity_metrics` | Cyclomatic complexity, nesting depth, LOC, parameter count |
| `get_cohesion_metrics` | LCOM4 cohesion score with method cluster breakdown (SRP analysis) |
| `find_shared_members` | Private members used by multiple public methods (extraction planning) |
| `find_unused_symbols` | Symbols with zero references (dead code candidates) |
| `impact_analysis` | References, affected declarations, affected projects for a symbol |
| `find_type_mutations` | Mutating members and their external callers |

### Structural Refactoring (preview/apply)
| Tool | When to Use |
|------|-------------|
| `extract_type_preview/apply` | Move selected members into a new type (SRP refactoring) |
| `extract_interface_preview/apply` | Create an interface from a type's public members |
| `bulk_replace_type_preview/apply` | Replace all references to one type with another |
| `move_type_to_file_preview/apply` | Move a type from a multi-type file to its own file |
| `rename_preview/apply` | Rename a symbol across the solution |
| `split_class_preview` | Split a class into a new partial file |

## SRP Refactoring Workflow

The recommended workflow for Single Responsibility Principle refactoring:

```
1. ANALYZE:  get_cohesion_metrics  → Find types with LCOM4 > 1
2. PLAN:     find_shared_members   → Identify extraction dependencies
3. ASSESS:   find_consumers        → Understand blast radius
4. EXTRACT:  extract_type_preview  → Preview the member extraction
5. APPLY:    extract_type_apply    → Execute the extraction
6. ABSTRACT: extract_interface_preview → Create interface for the new type (optional)
7. MIGRATE:  bulk_replace_type_preview → Update consumers to use interface (optional)
8. VERIFY:   build_workspace       → Check compilation
9. TEST:     test_related_files    → Run affected tests
```

Use the `cohesion_analysis` prompt for a guided version of this workflow.

## Preview/Apply Pattern

Most write operations follow a two-step pattern:

1. **Preview** (`*_preview`) — Returns a diff and a `previewToken`. Inspect the changes.
2. **Apply** (`*_apply`) — Pass the `previewToken` to commit changes. Tokens expire after ~15 minutes.

If the workspace changes between preview and apply, the token becomes stale and the apply will fail.
Always call `build_workspace` after applying changes.

## Undo

`revert_last_apply(workspaceId)` rolls back the **most recent** `*_apply`
operation on a workspace. Coverage includes renames, code fixes, format,
organize usings, `apply_text_edit` / `apply_multi_file_edit`, and the
file-level apply tools (`create_file_apply`, `delete_file_apply`,
`move_file_apply`, `extract_interface_apply`, `extract_type_apply`,
`move_type_to_file_apply`). A second `revert_last_apply` call in the same
session does **not** undo the one before it — the history is depth-1.

### revert_last_apply — side-effect cleanup

`revert_last_apply` reverts **text edits** from the last `*_apply`. It does
**NOT** remove files created as a side effect of that apply. Specifically,
the extracted file that `extract_type_apply`, `extract_method_apply`, or
`extract_interface_apply` wrote to disk stays on disk after the revert —
the extracted symbol is restored to its original location, but the new
file is now an orphan. Any other `*_apply` that creates new files has the
same shape.

**Canonical follow-up:** call `delete_file_apply` on the extracted file to
finish the undo.

**Worked example:**

```text
extract_type_apply(..., newTypeName: "Foo")
  → moves Foo out of Bar.cs into new file Foo.cs.
revert_last_apply(workspaceId)
  → Bar.cs is restored (Foo's members move back in),
    BUT Foo.cs still exists on disk (now empty of the extracted symbol
    or a leftover scaffold, depending on what the extract wrote).
delete_file_apply(workspaceId, filePath: ".../Foo.cs")
  → removes Foo.cs. Workspace is now byte-identical to the pre-extract state.
```

The same pattern applies to `extract_method_apply` (if the method lands in
a new partial file) and `extract_interface_apply` (the new interface file
on disk).

## Error Recovery

- **"Preview token is stale"** — The workspace changed since the preview. Re-run the preview.
- **"Workspace is not loaded"** — Call `workspace_load` first.
- **"Symbol not found"** — Verify file path and line/column. Use `symbol_search` to find the correct location.
- **Build failures after refactoring** — Check `project_diagnostics` for the specific errors, then use `code_fix_preview` for automated fixes.
