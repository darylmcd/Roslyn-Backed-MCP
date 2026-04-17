# Bootstrap Read-Tool Primer

<!-- purpose: Canonical pattern→tool cheat sheet. Applies to EVERY session including
     bootstrap self-edit sessions on this repo. Read this at session start before
     reaching for Grep / Bash: dotnet build / Bash: dotnet test. -->

## The write-side restriction — narrow, not blanket

The `bootstrapCaveat` that appears in this repo's plan `state.json` files and executor
prompts restricts write-side `*_apply` tools only — and only when the running MCP
server binary is the **same** binary whose source tree you're editing. The two shapes
in practice:

- **Main-checkout self-edit** (you're running `dotnet run --project src/RoslynMcp.Host.Stdio`
  against the checkout you're editing): `*_apply` is forbidden. The binary under edit
  is servicing the call, so the MSBuildWorkspace snapshot goes stale mid-apply.
- **Worktree self-edit** (subagent session inside `.worktrees/<id>/` running against
  the installed global tool at `%USERPROFILE%\.dotnet\tools\roslynmcp.exe`): `*_apply`
  is safe. The global binary is a distinct, already-built artifact; edits to the
  worktree source tree do not mutate it. Load the worktree's own `RoslynMcp.slnx`, use
  `workspace_reload` if a downstream call needs a refreshed snapshot — that's the
  ordinary peer-repo discipline, not a bootstrap-specific constraint.

Worktree-based backlog-sweep subagents (the default workflow per `ai_docs/workflow.md`)
fall into the second case. They may use `*_apply` tools when those are the correct tool
for the refactor.

**Every read-side tool is safe and strongly preferred over the generic alternatives**,
even on this repo. The list below is the pattern→tool mapping that should be your
default when working on any C# task, bootstrap or not.

## Pattern → tool (read-side — always safe)

| When you want to… | Use this Roslyn MCP tool | Fallback (only if tool disconnected) |
|---|---|---|
| **Find callers of a method / property / field** | `find_references` with `metadataName` or `filePath+line+column` | `Grep` for the simple name (lossy — matches same-simple-name symbols in other types) |
| **Find consumers of a type / interface** | `find_type_usages` or `find_consumers` | `Grep` for `: TypeName` + `new TypeName(` + `TypeName ` |
| **Find implementations of an interface** | `find_implementations` | `Grep` for `: IMyInterface` |
| **Find overrides of a virtual / abstract method** | `find_overrides` | `Grep` for the method name |
| **Find the base members a symbol overrides** | `find_base_members` | — (no good grep equivalent) |
| **Search for a symbol by name (exact / fuzzy / FQN)** | `symbol_search` (v1.20.0+ handles FQN via `QualifiedNameOnlyFormat`) | `Grep` for the identifier |
| **Get a symbol's full metadata at a position** | `symbol_info` (v1.20.0+ strict by default; pass `allowAdjacent=true` for the legacy lenient shape) | `Read` the file + eyeball |
| **Enumerate the public surface of a file** | `document_symbols` | `Grep` for `public ` |
| **Get the containing method / type of a cursor position** | `enclosing_symbol` | `Read` + count braces |
| **Verify a C# edit compiles (fast, per-workspace)** | `compile_check` — structured diagnostics, <1s on loaded workspace | `Bash: dotnet build` — full MSBuild cycle, ~5-30s |
| **Run the tests related to touched files** | `test_related_files` → `test_run --filter` | `Bash: dotnet test --no-build` — full suite |
| **Get workspace-wide diagnostics** | `project_diagnostics` (scoped to a project) | `Bash: dotnet build` |
| **Discover what an error code means + fixes** | `diagnostic_details` + `code_fix_preview` | Web search |
| **Check code-fix / refactor availability at a position** | `get_code_actions` | — (IDE-only otherwise) |
| **Understand complexity / cohesion / coupling** | `get_complexity_metrics` / `get_cohesion_metrics` | Eyeball the file |
| **Find dead code / unused symbols** | `find_unused_symbols` | — (no grep equivalent) |
| **Find tests that cover a symbol** | `test_related` (v1.20.0+ walks implementations via `SymbolFinder.FindReferencesAsync`) | `Grep` for the symbol name in test files |

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

- **Worktree self-edit** (the default workflow for backlog-sweep subagents and any
  session running against the installed global `roslynmcp` tool): use the full
  preview → apply flow. `*_apply` is safe in a worktree because the running binary is
  the installed dotnet tool, not the worktree source.
- **Main-checkout self-edit** (running `dotnet run --project src/RoslynMcp.Host.Stdio`
  against the checkout you're editing): do NOT use the `*_apply` half. The
  `*_preview` half is still useful for visualizing the proposed diff before you
  hand-edit; the apply step must be `Edit` / `Write` instead.
- **Every other C# repo**: prefer the full preview → apply flow over hand-editing.

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

## Anti-patterns (session evidence across 4 reproductions)

These are the shortcuts agents reach for and should not:

1. **`Bash: dotnet build` for post-edit verify.** Use `compile_check` instead —
   identical diagnostic coverage, 5–30× faster on a loaded workspace. `dotnet build`
   is only correct for the final CI-parity check via `verify-release.ps1`.
2. **`Bash: dotnet test --no-build` for post-edit verify.** Use `test_related_files`
   to derive the targeted filter, then `test_run --filter "<filter>"`. Full-suite
   `dotnet test` is only correct for the final CI-parity check.
3. **`Grep` for "who calls this method".** Use `find_references` with `metadataName`.
   Grep matches `SymbolFinder.FindReferencesAsync` when you want
   `MySymbolService.FindReferencesAsync`; `find_references` disambiguates on symbol
   identity.
4. **`Grep` for "find the type by name".** Use `symbol_search` — it handles
   camelCase / substring / FQN natively.
5. **Reading a file top-to-bottom to find all `catch` blocks that handle a given
   exception.** `find_references` on the exception type + visual scan of the hits
   is faster and won't miss buried `catch (AggregateException)` shapes.

## Self-diagnosis checklist before reaching for Grep / Bash

Ask yourself:

- Is my task phrased as "find / search / locate / enumerate"? → read-side tool.
- Is my task phrased as "verify / compile / test"? → read-side tool.
- Is my task phrased as "apply / rewrite / replace / move"? → write-side. In a
  worktree session (default for backlog-sweep subagents): use the Roslyn MCP `*_apply`
  tool. In a main-checkout self-edit session: fall back to `Edit` / `Write` with
  `compile_check` as the verify loop. `*_preview` is useful in both cases before
  committing.

If you find yourself typing `Grep` for a symbol name or `Bash: dotnet build` on a
repo that's already loaded in a workspace session, stop and pick the tool from the
table above.

## When the Roslyn MCP server is disconnected

Check `server_info` or try any `mcp__roslyn__*` tool — if the response is "server
not connected", then the fallback column of the table above is appropriate. Log the
disconnect (the consumer-repo convention is a one-line note in the PR description)
and follow up with `mcp-connection-session-resilience` diagnostics.

## Further reading

- `ai_docs/runtime.md` — canonical Roslyn MCP client policy (operational constraints)
- `ai_docs/domains/tool-usage-guide.md` — long-form decision tree for every tool, plus
  the [Verification workflow](domains/tool-usage-guide.md#verification-workflow-post-edit-default)
  section that promotes the `compile_check` → `test_run --filter` → `format_check`
  triple as the default post-edit loop (shell commands are CI-parity-only fallbacks).
- `ai_docs/backlog.md` — `bootstrap-read-only-roslyn-mcp-checklist-for-self-edit-sessions`
  is the backlog row that tracks adoption of this primer across sessions
