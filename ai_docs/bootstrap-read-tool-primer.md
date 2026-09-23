# Bootstrap Read-Tool Primer

<!-- purpose: Canonical pattern→tool cheat sheet for EVERY session (incl. bootstrap self-edit); read before reaching for Grep / Bash: dotnet build / Bash: dotnet test. -->

## Bootstrap request contract

1. Inspect the live `workspace_load` schema before calling it. The required argument is **`path`**, not `solutionPath`, `filePath`, or `workspacePath`.
2. For this checkout, call `workspace_load` with:

   ```json
   { "path": "C:/Code-Repo/Roslyn-Backed-MCP/RoslynMcp.slnx" }
   ```

   In another worktree, resolve that worktree's absolute `RoslynMcp.slnx` path first.
3. Keep the returned `workspaceId`. `isLoaded=true` confirms the file loaded even when `isReady=false`; inspect readiness diagnostics separately.
4. `WORKSPACE_UNRESOLVED_ANALYZER`: build the named analyzer project (or `build_workspace`), then `workspace_reload` and confirm `isReady=true`.
5. Correct malformed arguments from the live schema before retrying.
   - Verify an existing repository solution locally before requesting operator input.
   - A generic `FileNotFound` envelope alone does not prove the supplied path failed an existence check.

## Write-side rule

- Read-side tools are always safe and strongly preferred over generic alternatives, on this repo too.
- The write-side restriction is narrow: it applies only when the running server binary is the same one whose source you are editing (main-checkout self-edit).
- Worktree sessions (default for backlog-sweep subagents) and other repos use the full preview → apply flow.
- Canonical per-shape table: [runtime.md § Write-side by session shape](runtime.md#write-side-by-session-shape). Machine-consumed form: `selfEditCaveat` in [prompts/backlog-sweep-addenda.md](prompts/backlog-sweep-addenda.md).

## Pattern → tool (read-side — always safe)

| When you want to… | Use this Roslyn MCP tool | Fallback (only if tool disconnected) |
|---|---|---|
| **Find callers of a method / property / field** | `find_references` with `metadataName` or `filePath+line+column` | `Grep` for the simple name (lossy — matches same-simple-name symbols in other types) |
| **Find consumers of a type / interface** | `find_type_usages` or `find_consumers` | `Grep` for `: TypeName` + `new TypeName(` + `TypeName ` |
| **Find implementations of an interface** | `find_implementations` | `Grep` for `: IMyInterface` |
| **Find overrides of a virtual / abstract method** | `find_overrides` | `Grep` for the method name |
| **Find the base members a symbol overrides** | `find_base_members` | — (no good grep equivalent) |
| **Search for a symbol by name (exact / fuzzy / FQN)** | `symbol_search` (handles FQN) | `Grep` for the identifier |
| **Get a symbol's full metadata at a position** | `symbol_info` (strict by default; `allowAdjacent=true` for the lenient shape) | `Read` the file + eyeball |
| **Enumerate the public surface of a file** | `document_symbols` | `Grep` for `public ` |
| **Get the containing method / type of a cursor position** | `enclosing_symbol` | `Read` + count braces |
| **Verify a C# edit compiles (fast, per-workspace)** | `compile_check` — structured diagnostics, <1s on loaded workspace | `Bash: dotnet build` — full MSBuild cycle, ~5-30s |
| **Run the tests related to touched files** | `test_related_files` → `test_run --filter` | `Bash: dotnet test --no-build` — full suite |
| **Get workspace-wide diagnostics** | `project_diagnostics` (scoped to a project) | `Bash: dotnet build` |
| **Discover what an error code means + fixes** | `diagnostic_details` + `code_fix_preview` | Web search |
| **Check code-fix / refactor availability at a position** | `get_code_actions` | — (IDE-only otherwise) |
| **Understand complexity / cohesion / coupling** | `get_complexity_metrics` / `get_cohesion_metrics` | Eyeball the file |
| **Find dead code / unused symbols** | `find_unused_symbols` | — (no grep equivalent) |
| **Find tests that cover a symbol** | `test_related` (walks implementations via `SymbolFinder.FindReferencesAsync`) | `Grep` for the symbol name in test files |

## Pattern → skill (read-side composites)

| When you want to… | Skill |
|---|---|
| Quick health check for the whole solution | `roslyn-mcp:analyze` |
| Find complexity hotspots | `roslyn-mcp:complexity` |
| Full semantic review before a PR | `roslyn-mcp:review` |
| Dead code cleanup sweep | `roslyn-mcp:dead-code` |
| Diagnostic-driven triage loop | `roslyn-mcp:explain-error` |
| Test discovery + failure triage | `roslyn-mcp:test-triage` |
| Test coverage analysis | `roslyn-mcp:test-coverage` |

## Pattern → tool (write-side)

Session-shape rule: see [Write-side rule](#write-side-rule). `*_preview` is useful in every shape for visualizing the diff before committing to an edit.

| When you want to… | Tool |
|---|---|
| Rename a symbol across the solution | `rename_preview` → `rename_apply` |
| Extract a type, interface, or method | `extract_type_*`, `extract_interface_*`, `extract_method_*` |
| Move a type between files / projects | `move_type_to_file_*`, `move_type_to_project_preview` |
| Change a method's signature (add / remove / rename param) | `change_signature_preview` |
| Apply a diagnostic code fix | `code_fix_preview` → `code_fix_apply` |
| Bulk-replace one type with another | `bulk_replace_type_*` |
| Organize usings | `organize_usings_*` |
| Format a document or range | `format_document_*`, `format_range_*` |

## Anti-patterns

1. **`Bash: dotnet build` for post-edit verify.** Use `compile_check` (same diagnostic coverage, much faster on a loaded workspace). `dotnet build` is only for the final CI-parity check via `verify-release.ps1`.
2. **`Bash: dotnet test --no-build` for post-edit verify.** Use `test_related_files` then `test_run --filter "<filter>"`. Full-suite `dotnet test` is only for the final CI-parity check.
3. **`Grep` for "who calls this method".** Use `find_references` with `metadataName`; grep conflates same-simple-name symbols (`SymbolFinder.FindReferencesAsync` vs `MySymbolService.FindReferencesAsync`).
4. **`Grep` for "find the type by name".** Use `symbol_search` (camelCase / substring / FQN).
5. **Reading a file top-to-bottom to find `catch` blocks for an exception.** Use `find_references` on the exception type; it will not miss buried `catch (AggregateException)` shapes.

## Self-diagnosis before reaching for Grep / Bash

- "find / search / locate / enumerate" → read-side tool.
- "verify / compile / test" → read-side tool.
- "apply / rewrite / replace / move" → write-side; pick the path per the [Write-side rule](#write-side-rule).
- Typing `Grep` for a symbol name or `Bash: dotnet build` on a repo already loaded in a workspace session: stop and use the table above.

## When the Roslyn MCP server is disconnected

- Scan the live tool surface for `server_info` candidates; identify the Roslyn server by response shape (`connection.state`, `catalogVersion`, `surface.*`); pin that candidate's client-assigned prefix for every later Roslyn call.
- No candidate returns a Roslyn-shaped response: the fallback column above applies. Log the disconnect (consumer-repo convention: one line in the PR description).
- Do **not** infer connectivity from the deferred-tool catalog (a client may advertise tool names with schemas unloaded while the server is down).
- Authoritative probes and non-signals: [runtime.md § Connection-State Signals](runtime.md#connection-state-signals).

## Further reading

- [runtime.md](runtime.md) — canonical Roslyn MCP client policy.
- [domains/tool-usage-guide.md](domains/tool-usage-guide.md) — decision tree for every tool; [Verification workflow](domains/tool-usage-guide.md#verification-workflow-post-edit-default) is the default post-edit loop (shell commands are CI-parity-only fallbacks).
