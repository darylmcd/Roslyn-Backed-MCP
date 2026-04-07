# MCP Server Audit Report — Run 2 (supplementary)

## Header
- **Date:** 2026-04-07
- **Audited solution:** ITChatBot.sln
- **Audited revision:** worktree `worktree-deep-review-audit` from main @ c09d7ff
- **Entrypoint loaded:** `C:\Code-Repo\IT-Chat-Bot\.claude\worktrees\deep-review-audit\ITChatBot.sln`
- **Audit mode:** full-surface (second pass — targeted at phases compressed in run 1)
- **Isolation:** same disposable worktree as run 1
- **Client:** Claude Code (CLI)
- **Workspace id:** aedf3104e91f4c2abcaf2ab9c08637c2 (distinct from run 1's 886911b5…)
- **Server:** roslyn-mcp 1.6.0+13e135b7 (catalog 2026.03)
- **Lock mode at audit time:** `legacy-mutex` (still no RW-lock flag set)
- **Debug log channel:** `no` (Claude Code does not surface MCP log notifications)
- **Relationship to prior report:** Supplementary to `20260407T211317Z_itchatbot_legacy-mutex_mcp-server-audit.md`. Covers phases **compressed** in run 1: real Phase 6 apply chain, Phase 9 revert verification, Phase 10 file operations, Phase 12 scaffolding, Phase 13 project mutation, and deeper Phase 14/17 coverage. **Does not supersede run 1** — read both for full signal.
- **Findings index in run 2:** FLAG-016 through FLAG-021 (continues the numbering from run 1).

## Scope covered in run 2 (delta from run 1)
| Phase | Run 1 status | Run 2 status |
|-------|---------------|---------------|
| 0 — setup | exercised | re-loaded workspace to verify FLAG-001 reproduces (it does) |
| 6 — refactor apply | **N/A** (FLAG-011 blocked organize_usings) | `apply_text_edit` exercised (with manual compensating revert because `revert_last_apply` cannot revert disk-direct edits) |
| 9 — undo verification | N/A | Confirmed `apply_text_edit` is not reachable by `revert_last_apply`; see FLAG-018 |
| 10 — file/cross-project previews | skipped-safety | `move_type_to_file_preview`, `create_file_preview` — both exercised |
| 12 — scaffolding | skipped-safety | `scaffold_type_preview`, `scaffold_test_preview` — both exercised |
| 13 — project mutation | skipped-safety | `add_package_reference_preview`, `set_project_property_preview` — both exercised (both previews, no applies) |
| 14 — navigation | minimal | `enclosing_symbol`, `goto_type_definition`, `get_completions` (with `filterText`), `document_symbols`, `symbol_info`, `callers_callees` |
| 17 — negative tests | moderate | Added: invalid identifier rename, out-of-range line, empty snippet/script |

## New findings (FLAG-016 through FLAG-021)

### FLAG-016 — `move_type_to_file_preview` leaves blank lines at the top of the new file
- **Tool:** `move_type_to_file_preview`
- **Inputs:** `sourceFilePath=src/adapters/SysLogServer/SysLogServerApiClient.cs, typeName=SysLogQueryResponse`
- **Expected:** New file should start with `namespace ITChatBot.Adapters.SysLogServer;` on line 1 (or with the trailing usings from the source file, if any are needed).
- **Actual:** Diff shows 5 leading blank-line entries before the namespace declaration in the newly-created `SysLogQueryResponse.cs`. Same class of bug as **FLAG-011** — the refactor machinery is removing trivia (blank lines + usings) from the source context but inserting empty lines in the new file instead of compacting.
- **Severity:** incorrect result / cosmetic apply hazard. If the user applies this and then runs `format_document_apply`, the format tool (per run 1 observations) returns empty Changes on already-formatted files, so the 5 blank lines would persist.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-017 — `set_project_property_preview` produces malformed XML by squishing a new element onto the closing `</PropertyGroup>` line
- **Tool:** `set_project_property_preview`
- **Inputs:** `projectName=ITChatBot.Adapters.Abstractions, propertyName=LangVersion, value=latest`
- **Expected:** New element on its own line with proper two-space or tab indentation, matching the surrounding style:
  ```xml
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  ```
- **Actual:** Diff shows `  <LangVersion>latest</LangVersion></PropertyGroup>` — the new element is glued to the closing tag on the same line. The XML is still *valid* but it is not idiomatic MSBuild formatting and will fail review in any team with a csproj style policy.
- **Severity:** incorrect result (diff quality). High impact because this is the happy-path flow for any automated csproj mutation.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-018 — `apply_text_edit` + `revert_last_apply` chain does not satisfy Phase 9's design
- **Tools:** `apply_text_edit`, `revert_last_apply`
- **Context:** Phase 9 of the deep-review prompt asks for an audit-only apply that can be cleanly reverted so `revert_last_apply` gets exercised against a fresh target.
- **Expected:** `apply_text_edit` should either register itself in the undo stack OR the schema should instruct callers to use `format_document_apply` / `rename_apply` / etc. for Phase 9's reversible-apply slot.
- **Actual:** `apply_text_edit` schema (verified: "DISK-DIRECT, NOT REVERTIBLE (UX-004)") plus `revert_last_apply` correctly returns `{reverted: false, message: "Nothing has been applied..."}`. The behavior is consistent with the documentation, but it means the Phase 9 sub-step "perform exactly one low-impact Roslyn apply whose reversal is safe" has a very narrow set of valid candidates (format_document_apply or rename_apply), and both of those frequently return empty previews on well-maintained solutions. On this repo, `format_document_preview` on every Phase 6-touched file returns empty `Changes` — so Phase 9 has no reversible target to exercise.
- **Severity:** prompt vs server contract mismatch. Not strictly a server bug, but the combination of (a) documented-disk-direct edit tools and (b) the Phase 9 "do one reversible apply" requirement creates a dead end on clean repos.
- **Recommendation:** `apply_text_edit` / `apply_multi_file_edit` should participate in the undo stack, OR the prompt's Phase 9 should explicitly say "if no reversible candidate exists, skip and record N/A."
- **Reproducibility:** always
- **Lock mode:** legacy-mutex

### FLAG-019 — PreToolUse hook blocks cross-session preview-token reuse (local hook, not a server bug)
- **Context:** This run used a preview token (`fc813cda0a3047b0bef43be1e0ccb02e`) that was minted in the run-1 session. A local PreToolUse hook configured in the user's settings refused the `format_document_apply` call with:
  > Cannot verify preview without access to transcript. The transcript_path provided (...) cannot be read by this evaluation function. To proceed safely, the agent must call the matching *_preview tool (mcp__roslyn__format_document_preview) first and receive explicit user confirmation of the changes before applying mutations.
- **Expected for the server:** `format_document_apply` with a stale cross-session token should reject with a server-side staleness error like `{error: true, category: "StaleToken"}`.
- **Actual for the server:** Not observable — the client hook intercepted before the request reached the server.
- **Severity:** informational. Not a roslyn-mcp bug — it is the user's safety hook. Worth recording because it affects how the prompt's Phase 9 chain runs in practice for anyone using similar hooks: `format_document_preview` must be called inside the same conversation as `format_document_apply`, even if both point at the same workspace state.
- **Reproducibility:** always (with this hook configured)
- **Lock mode:** legacy-mutex

### FLAG-020 — **`rename_preview` accepts illegal C# identifiers** (major)
- **Tool:** `rename_preview`
- **Inputs:** `filePath=src/adapters/Abstractions/AdapterRegistry.cs, line=7, column=21, newName="123InvalidIdentifier"` (starts with a digit → not a valid C# identifier)
- **Expected:** Structured error like `{error: true, category: "InvalidArgument", message: "'123InvalidIdentifier' is not a valid C# identifier"}`.
- **Actual:** Full preview returned with 5 file changes (production, tests, DI registration), rewriting `AdapterRegistry` → `123InvalidIdentifier` everywhere including `services.AddSingleton<IAdapterRegistry, 123InvalidIdentifier>();`. **If applied, this would cause catastrophic compilation failure across production and test code.**
- **Severity:** **incorrect result / missing validation**. High severity because `rename_preview` is the happy-path for rename refactoring and an AI client generating plausible but illegal identifiers (e.g. from a bad regex or a stray leading digit) would get a clean-looking preview and, if auto-applying, break the solution. No `Warnings` entry in the response indicating the name is illegal.
- **Reproducibility:** always
- **Lock mode:** legacy-mutex
- **Recommended fix:** run `SyntaxFacts.IsValidIdentifier(newName)` in `rename_preview` before generating changes; return a structured InvalidArgument error when it is false.

### FLAG-021 — **`workspace_close` throws `ObjectDisposedException` but actually succeeds** (major)
- **Tool:** `workspace_close`
- **Inputs:** `workspaceId=aedf3104e91f4c2abcaf2ab9c08637c2` (the active run-2 workspace, freshly loaded in this session and used by ~20 preceding tool calls that all succeeded)
- **Expected:** Clean `{success: true, workspaceId: ...}` response.
- **Actual:** Structured error:
  ```json
  {
    "error": true,
    "category": "InvalidOperation",
    "tool": "unknown",
    "message": "Invalid operation: Cannot access a disposed object.\r\nObject name: 'System.Threading.SemaphoreSlim'..",
    "exceptionType": "ObjectDisposedException"
  }
  ```
  **However**, calling `workspace_list` immediately afterwards returns `{count: 0, workspaces: []}` — so the close *actually succeeded*. The exception is raised on a post-close lock release against an already-disposed `SemaphoreSlim`.
- **Severity:** **incorrect result / crash-visible success**. Agents cannot trust the close response — they have to call `workspace_list` to verify the workspace is actually gone. Combined with FLAG-015 (`tool: "unknown"` in error wrapper), the failure is also hard to attribute.
- **Reproducibility:** always in this session. The run-1 `workspace_close` call on workspace id `886911b578654b4b85cca952ad292669` returned cleanly earlier — so the bug is likely triggered by the sequence: **workspace_load → workspace_close → workspace_load (different id, same path) → workspace_close**. The second close reuses a disposed semaphore somewhere in the cleanup path.
- **Likely root cause (speculative):** the per-workspace `SemaphoreSlim` dictionary in `WorkspaceExecutionGate` may be keyed by the loaded path (or by a cached key) rather than the workspace id, so the second workspace_load reuses a stale disposed semaphore from the first workspace_close.
- **Lock mode:** legacy-mutex
- **Recommended fix:** investigate `WorkspaceExecutionGate.RunPerWorkspaceAsync` and the per-workspace semaphore lifetime; ensure that after `workspace_close` fully disposes the semaphore, the next `workspace_load` creates a *new* semaphore keyed by the new workspace id.

## Tools verified working in run 2 (new coverage)
- `workspace_load` (second load in same session — reproduced FLAG-001 identically)
- `move_type_to_file_preview` — moves type successfully; FLAG-016 blank-line artifact
- `create_file_preview` — clean diff
- `scaffold_type_preview` — creates `public class AuditScaffoldProbe {}`; **improvement**: should default to `internal sealed`
- `scaffold_test_preview` — generates valid xunit test stub with `[Fact]` attribute; uses `default(IEnumerable<ISourceAdapter>)` as constructor arg which would throw NRE if actually executed — improvement: generate a stub that compiles AND runs green (use `Array.Empty<T>()` etc.)
- `add_package_reference_preview` — clean diff, correct ItemGroup
- `set_project_property_preview` — FLAG-017 formatting bug
- `apply_text_edit` — insert + delete both succeeded; not revertible by design (FLAG-018)
- `compile_check project-scoped limit=5` — 89 ms for `ITChatBot.Adapters.Abstractions`, PASS
- `enclosing_symbol` — resolves to constructor correctly from inside foreach body
- `goto_type_definition` — correctly navigates from variable declaration to type
- `get_completions filterText="To"` — returns 8 items, IsIncomplete=false; **observation**: non-in-scope types (`ToBase64Transform` from System.Security.Cryptography) rank near in-scope `ToString` — improvement: weight in-scope completions above namespace-qualified externals
- `document_symbols` — accurate hierarchical view (3 fields + constructor + 4 methods with correct line spans)
- `symbol_info metadataName` — returns full info including XML doc comment
- `callers_callees` on `GetAll()` — 23 callers across 13 files (production + tests), `callees=[]` for the expression-bodied method
- `analyze_snippet code=""` — returns `IsValid=true, ErrorCount=0, DeclaredSymbols=null`; defensible
- `evaluate_csharp code=""` — returns `Success=true, ResultValue="null"`; defensible (empty ≈ null)
- `go_to_definition line=9999` — clean structured InvalidArgument error with exact line count: "Line 9999 is out of range. The file has 48 line(s)." **Excellent error quality**
- `revert_last_apply` (after disk-direct edit) — returns `{reverted: false, message: "No operation to revert..."}` cleanly

## Run-2 coverage ledger delta
| Tool | Run 1 status | Run 2 status |
|------|--------------|--------------|
| `move_type_to_file_preview` | skipped-safety | **exercised-preview-only** — FLAG-016 |
| `create_file_preview` | skipped-safety | **exercised-preview-only** |
| `scaffold_type_preview` | skipped-safety | **exercised-preview-only** |
| `scaffold_test_preview` | skipped-safety | **exercised-preview-only** |
| `add_package_reference_preview` | skipped-safety | **exercised-preview-only** |
| `set_project_property_preview` | skipped-safety | **exercised-preview-only** — FLAG-017 |
| `apply_text_edit` | skipped-safety | **exercised-apply** (insert + manual revert) |
| `enclosing_symbol` | skipped-safety | **exercised** |
| `goto_type_definition` | skipped-safety | **exercised** |
| `get_completions` | skipped-safety | **exercised** |
| `document_symbols` | exercised (implied) | **exercised** (explicit on AdapterRegistry.cs) |
| `symbol_info` | skipped-safety | **exercised** |
| `callers_callees` | skipped-safety | **exercised** |
| `rename_preview` | exercised (same-name no-op) | **exercised** (invalid identifier) — **FLAG-020** |
| `workspace_close` | exercised (clean) | **exercised** — **FLAG-021** (succeeds but throws) |

## Improvement suggestions (run 2 delta)
16. **`rename_preview` must validate identifier legality** before generating changes — blocker for any auto-apply rename pipeline.
17. **`workspace_close` must not throw `ObjectDisposedException` after a second load/close pair on the same path** — either return clean success, or return a structured WorkspaceGone category that an agent can distinguish from a real crash.
18. **`move_type_to_file_preview` and `set_project_property_preview` share a common formatting/indentation bug** (blank-line artifacts + element-on-closing-tag). Consider a shared diff-builder helper that canonicalizes trivia.
19. **`scaffold_type_preview` default modifiers** — modern .NET convention is `internal sealed class` unless the caller opts into `public` or `open`. Current default is `public class`.
20. **`scaffold_test_preview` generated stub is not necessarily compilable at runtime** — using `default(IEnumerable<ISourceAdapter>)` for a non-null parameter will NRE the moment anyone runs it. Prefer `Array.Empty<T>()` / `new T[0]` / `[] as IEnumerable<T>` when the parameter type is an empty-constructible interface.
21. **`get_completions` ranking** — in-scope instance members should rank above namespace-qualified external types for a given `filterText` prefix.

## Final status of the audit worktree
```
$ git status --porcelain
?? ai_docs/audit-reports/
```
Only the audit-reports directory is untracked. Source tree is clean (`apply_text_edit` probe was manually reverted). `AdapterRegistry.cs` restored to the original 48-line form. All previews from run 2 were preview-only and no project file on disk was mutated by this session.

## Summary of cumulative findings (runs 1 + 2)
- **Run 1 findings:** FLAG-001 through FLAG-015 (14 distinct, 1 missing number)
- **Run 2 findings:** FLAG-016 through FLAG-021 (6 new)
- **Total distinct findings:** 20
- **Highest-severity new bugs from run 2:**
  - **FLAG-020** — `rename_preview` accepts illegal C# identifiers (would break build on apply)
  - **FLAG-021** — `workspace_close` throws `ObjectDisposedException` but actually succeeds (visibility/contract)
- **Phase 6 apply:** `apply_text_edit` exercised (insert + manual delete), disk-direct path confirmed working. Net zero file changes.
- **Phase 8b concurrency matrix:** still `skipped-pending-second-run`; dual-mode audit still requires the four-step operator dance with `ROSLYNMCP_WORKSPACE_RW_LOCK=true`.
- **Phase 16 prompts:** still `blocked` — Claude Code does not expose MCP prompts to the agent loop.
- **Phase 18 regression check:** FLAG-010 still doubles as a confirmed regression from the prompt's appendix (FLAG-C / UX-001 fixed in v1.7+, server is v1.6.0).

## Report path note
Both run-1 and run-2 reports live at:
```
<itchatbot-worktree>/ai_docs/audit-reports/
  20260407T211317Z_itchatbot_legacy-mutex_mcp-server-audit.md  (run 1 canonical)
  20260407T213736Z_itchatbot_legacy-mutex_mcp-server-audit.md  (run 2 supplementary, this file)
```
Both should be copied into `<roslyn-backed-mcp-root>/ai_docs/audit-reports/` before generating any rollup. Each is self-contained; prefer reading them together for complete surface coverage.
