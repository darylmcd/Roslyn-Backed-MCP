# Backlog sweep plan — 20260509T181343Z

**Generated:** 2026-05-09T18:13:43Z
**Backlog snapshot:** 2026-05-09T15:45:42Z
**Initiative count:** 2
**Anchor verification:** performed

The backlog is small after the 2026-05-08 sweep landed. Open tables: 1 Medium + 3 Low. Two rows are plan-ready — one freshly unblocked Medium correctness/UX row, one Low with a cheap doc path. The remaining two Low rows have explicit weak-evidence flags and the four Defer rows have unmet triggers — all skipped here with explicit reasons. `count=10` cap is moot.

## Initiatives (in order)

### 1. host-middleware-tools-namespace-cycle

| Field | Content |
|---|---|
| Status | pending |
| Priority | Low |
| Backlog rows closed | `host-middleware-tools-namespace-cycle` |
| Diagnosis | The 2026-05-08 stress-audit confirmed a `Host.Stdio.Middleware` ↔ `Host.Stdio.Tools` namespace cycle via `get_namespace_dependencies(circularOnly=true)`. Anchors: `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` and the broader `src/RoslynMcp.Host.Stdio/Middleware/` ↔ `src/RoslynMcp.Host.Stdio/Tools/` boundary. The row offers two paths: (a) document the cycle in `ai_docs/architecture.md` § Known Gaps with rationale, OR (b) introduce a tool-dispatch envelope abstraction. Path (a) is the cheap, shippable, no-behavior-change deliverable; path (b) is a real refactor that should become a separate row only if the cycle starts blocking work. Plan path (a). |
| Approach | Add a "Known architecture gaps" subsection (or extend an existing one) in `ai_docs/architecture.md` documenting (1) the cycle's two endpoints, (2) why it exists today (middleware needs to inspect tool metadata; tools need to declare middleware-relevant attributes), (3) the cost-of-fix (refactor to envelope) versus cost-of-living-with-it (none observed in practice), (4) the trigger that would force action (middleware-side feature requiring a new tool category). Single doc edit. |
| Scope | Production files touched: 0. Docs files touched: 1 — `ai_docs/architecture.md`. Test files modified or added: 0. Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | Doc-only — risk is wording. Verify the architecture.md doc-link verifier (`./eng/verify-ai-docs.ps1`) passes after the edit. |
| Validation | `./eng/verify-ai-docs.ps1` passes. The row's own validation shape (`get_namespace_dependencies(circularOnly=true)` returns empty) is NOT met by path (a) — it is the path (b) acceptance criterion. Path (a) closes the row by accepting the cycle with documented rationale; the regression-test field in the backlog row applies only to path (b). |
| Performance review | N/A — doc-only. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Documented the accepted `Host.Stdio.Middleware` ↔ `Host.Stdio.Tools` namespace cycle in `ai_docs/architecture.md` § Known Gaps. |
| Backlog sync | Close rows: `host-middleware-tools-namespace-cycle`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

### 2. file-lock-aware-prompt-validation-guidance

| Field | Content |
|---|---|
| Status | pending |
| Priority | Medium |
| Backlog rows closed | `file-lock-aware-prompt-validation-guidance` |
| Diagnosis | Dependency `build-test-self-analyzer-file-lock` shipped 2026-05-08 in PR #563 (workspace-load shadow-copy + BuildService/TestRunnerService file-lock envelope). The shipped envelope surfaces `errorKind: FileLock` plus `MSB3027`/`MSB3021` markers for self-hosted analyzer-DLL contention, but downstream consumers (operator-facing prompts in `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs:117,137` and `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:13`, plus the maintainer audit prompt at `.claude/skills/mcp-server-stress/prompts/prompt.md:345,348,484`) still treat any failed `build_workspace`/`test_run` as a test-authoring problem and re-trigger validation in the same loaded workspace, re-acquiring the lock. The 2026-05-08 stress run reproduced this loop. Anchor verification: all four files exist; the `FileLock`/`MSB3027`/`MSB3021` strings are NOT yet present in `PromptMessageBuilder.cs` (executor will introduce them as part of this initiative — they are aspirational anchors from the row author, not stale references to existing code). |
| Approach | Two production edits + one prompt-skill edit + a focused regression test. (1) In `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs` (or whichever helper renders failure envelopes into prompt context), add a render branch that recognizes `failureEnvelope.errorKind == "FileLock"` (and the `MSB3027`/`MSB3021` text fallback) and renders a bypass-guidance block instead of the standard test-failure framing. The block should: (a) declare the failure infrastructure-class, (b) suggest `compile_check` / read-side evidence as the next step, (c) tell the operator to close+reload the workspace or run validation from an isolated process after `dotnet build-server shutdown`. (2) Update `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:13` (the `debug_test_failure` prompt path) to short-circuit the standard retry/diagnose loop when the most recent failure envelope was a `FileLock`. (3) Update `.claude/skills/mcp-server-stress/prompts/prompt.md:345,348,484` to mirror the same guidance for the maintainer audit prompt. (4) Add 1 prompt-rendering test exercising the FileLock envelope path and asserting the rendered prompt contains the bypass guidance string. |
| Scope | Production files touched: 2 — `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs`, `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs`. Skill content (under `.claude/skills/mcp-server-stress/prompts/prompt.md`) is also touched but it is shipped operator-skill content rather than production code; counted as 0 production files. Test files modified or added: 1 — extend an existing prompt-rendering test under `tests/RoslynMcp.Tests/` (likely `tests/RoslynMcp.Tests/Prompts/PromptMessageBuilderTests.cs` or add a focused fixture if no existing prompt-rendering tests exist). Files deleted: none. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | The exact name of the failure-envelope error-kind enum value is asserted by the row author but not yet verified in code — executor must locate the actual `errorKind` constant introduced by PR #563 and bind to it (string-match on `"FileLock"` is fine if no enum exists). The prompt-test infrastructure may not have an existing pattern for asserting on prompt-text contents — if so, add one focused harness rather than spreading the test-helper change across multiple files. |
| Validation | New regression test asserts: given a synthesized `FileLock` failure envelope, the rendered prompt contains the bypass-guidance block (not the standard test-failure framing). Run `mcp__roslyn__compile_check` on each edited file, focused prompt-rendering tests, then `./eng/verify-release.ps1 -Configuration Release`. Manual reproduction is not required — the regression test covers the symptom. |
| Performance review | N/A — text-rendering branch, no hot path. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Operator and audit prompts now recognize self-hosted analyzer-DLL file-lock failures (`errorKind: FileLock`, `MSB3027`/`MSB3021`) as infrastructure rather than test-authoring failures, and emit bypass guidance (close+reload workspace, prefer `compile_check` evidence, run from isolated process after `dotnet build-server shutdown`) instead of the standard retry/diagnose loop. |
| Backlog sync | Close rows: `file-lock-aware-prompt-validation-guidance`. Final implementation todo: `backlog: sync ai_docs/backlog.md`. |

## Items skipped

| Backlog row | Section | Reason |
|---|---|---|
| `tool-surface-pagination-or-tool-sets` | Low | Trigger not met. Live tool count is ~169 after PR #586; the row's stated trigger is ~200 tools OR external small-model discovery friction. Neither condition holds. The row carries an explicit "Weaker evidence — N until small-model discovery friction reported externally" flag. |
| `scaffolding-service-split-by-scaffold-type` | Low | Pure organizational refactor with explicit "Weaker evidence — defer until a concrete bug or modification motivates the split" flag from the 2026-05-09 sanity-check audit. The two recent ScaffoldingService bugs (`scaffold-test-internal-target-accessibility`, `scaffold-test-batch-nullable-constructor-output`) both shipped without needing the split; the file is large but tractable. Defer until a concrete forcing function lands. |
| `validate-locator-preflight-tool` | Defer | Re-evaluation window not yet reached. Row says "Re-evaluate after 2026-05-12"; today is 2026-05-09. The 7-day measurement of the schemaHint experiment must complete first. |
| `http-streamable-host-project` | Defer | No concrete remote-deployment driver (named users, auth/observability/tenancy plan staffed). Roadmap explicitly punts pending external ask. |
| `workspace-process-pool-or-daemon` | Defer | Representative 227-project OrchardCore profile (2026-04-26) did not justify daemon/process-pool. Defer pending a worse-profile signal. |
| `workspace-manager-cache-store-extraction` | Defer | Row was added 2026-05-09 with explicit "needs design note first" gating, citing the `parameter-object-preview-tool` precedent. The next deliverable is authoring `ai_docs/items/workspace-manager-cache-store-extraction-design.md` — a design-note pass, not a planner-emitted initiative. Surface as a one-off task when ready. |

## Self-vet

- No initiative closes more than 1 row.
- No initiative touches more than 4 production files (initiative 1 touches 0 prod files; initiative 2 touches 2).
- No initiative adds more than 1 test file.
- Both initiatives have `estimatedContextTokens` well under 80K.
- Both initiatives have an explicit `toolPolicy: edit-only`.
- Neither initiative does a rename/cross-cutting refactor; fanout pre-probe is recorded as `0` (initiative 1, doc-only) and `2` (initiative 2, matching `productionFilesTouched`). No `fanoutOversize` flag.
- Hotspot rule: neither initiative touches an addenda-listed hotspot file (`ServerSurfaceCatalog.*`, `ServiceCollectionExtensions.cs`, `WorkspaceManager.cs`).
- Source citations are plain inline code paths, not bracket-paren markdown links, so the doc-checker (`./eng/verify-ai-docs.ps1`) is satisfied.

## Next step

Run `/backlog-sweep:review` before `/backlog-sweep:execute`.
