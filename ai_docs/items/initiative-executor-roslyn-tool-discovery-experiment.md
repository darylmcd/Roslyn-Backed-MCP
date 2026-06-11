# initiative-executor-roslyn-tool-discovery-experiment — measure executor Roslyn first-hop bypass post-router

**row:** `initiative-executor-roslyn-tool-discovery-experiment` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `.claude/agents/initiative-executor.md`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.GuidedWorkflows.cs`
- `skills/refactor/SKILL.md`
- `skills/semantic-find/SKILL.md`

## Acceptance

- [ ] Measurement note created with sample source, semantic-first-hop counts, generic `Read`/`Grep`/`Edit` counts, `workspace_reload` counts, and a go/no-go decision
- [ ] If go: one follow-on implementation row added to inject the Roslyn-first stanza and validate it with a controlled refactor rerun

## Evidence

- The design brief (migrated below) specifies this row; `ai_docs/reports/20260521T043918Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`. **Corroboration (2026-06-08 retro):** a `/top-5-remediation` executor-style session (8fa58da6, 2026-05-30) did 10 `.cs` edits via `Edit`/`Grep` with Roslyn touched only for a `server_info` readiness probe; 6 of 9 Roslyn-touching sessions in the 14-day window reached Roslyn only for `server_info` — see `ai_docs/reports/20260608T203050Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §3#server_info-only. Source: 2026-06-04 discovery-sweep work-search + 2026-06-08 retro.

## Context

Measure whether refactoring initiative executors still bypass Roslyn semantic first-hop tools after `recommend_workflow`; only then edit the executor brief.

## Notes — migrated design brief (was `ai_docs/items/initiative-executor-roslyn-tool-discovery-brief.md`, v15 migration 2026-06-11)

# Initiative Executor Roslyn Tool Discovery Brief

<!-- purpose: Design the Roslyn-first brief for refactoring initiative executors. -->
<!-- scope: in-repo -->

## Evidence

The 2026-05-21 multi-session retro sampled five `initiative-executor`
refactoring subagents. Their tool mix was dominated by `Read`, `Grep`, `Bash`,
and `Edit`, while semantic Roslyn tools such as `find_references`,
`rename_preview`, `symbol_search`, `move_type_to_file_preview`, and
`extract_method_preview` were effectively unused for C# refactor initiatives.

The same sample showed 4-7 `workspace_reload` calls per subagent. The likely
cause is the subagent loop: edit files through generic `Edit`, notice the
workspace snapshot is stale, reload, then verify. `apply_text_edit` already
offers an MCP-owned edit path with compile verification and optional rollback,
but the executor brief does not put it in front of the subagent.

The evidence is still weak: five refactoring subagent samples, biased toward
backlog-sweep executions. That supports a focused experiment, not an immediate
permanent agent-contract rewrite.

## Roslyn-First Mapping

Use this mapping when an initiative touches C# files:

| Refactor shape | First-hop Roslyn tools | Notes |
|---|---|---|
| Find callers or impact | `find_references`, `find_consumers`, `test_related` | Prefer metadata/source locators over text search when the target symbol is known. |
| Rename symbol | `rename_preview` then `rename_apply` when the same-session preview policy is satisfied | If the executor's `toolPolicy` is `edit-only`, report that semantic apply is unavailable rather than approximating a solution-wide rename with text edits. |
| Move type | `move_type_to_file_preview` or `move_type_to_project_preview` before any manual file split | Preview should name every changed file before apply. |
| Extract method/type/interface | `extract_method_preview`, `extract_type_preview`, or `extract_interface_preview` | Use preview output to avoid hand-rolling syntax changes. |
| Localized in-place edit | `apply_text_edit` with `verify=true` and `autoRevertOnError=true` | This keeps the workspace snapshot consistent and reduces reload churn. |
| Compile sanity | `compile_check` before shell build | Shell build remains CI-parity/final validation, not the fast inner loop. |
| Test selection | `test_related_files` then `test_run --filter` | Fall back to shell test only when the tool reports a structured blocker. |

## Brief-Injection Plan

Do not edit `.claude/agents/initiative-executor.md` yet. First run a bounded
measurement experiment:

1. Sample refactoring-only initiative-executor sessions from the 30 days after
   `recommend_workflow` shipped.
2. Record semantic first-hop calls (`find_references`, `symbol_search`,
   `rename_preview`, move/extract previews, `apply_text_edit`) versus generic
   `Read`/`Grep`/`Edit` use.
3. Record workspace reload count per initiative.
4. If semantic first-hop usage remains low or reload count remains high, add a
   short Roslyn-first stanza to `.claude/agents/initiative-executor.md` using
   the mapping above.
5. Re-run a small controlled refactor initiative and compare tool mix and
   validation confidence.

## Follow-On Row

`initiative-executor-roslyn-tool-discovery-experiment` | Low | none | Measure whether refactoring initiative executors still bypass Roslyn semantic first-hop tools after `recommend_workflow`; only then edit the executor brief. Anchors: `.claude/agents/initiative-executor.md`, `ai_docs/reports/20260521T043918Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`, `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.GuidedWorkflows.cs`, `skills/refactor/SKILL.md`, `skills/semantic-find/SKILL.md`. Regression/output shape: create a measurement note with sample source, semantic-first-hop counts, generic `Read`/`Grep`/`Edit` counts, workspace_reload counts, and a go/no-go decision; if go, add one follow-on implementation row to inject the Roslyn-first stanza and validate it with a controlled refactor rerun. Evidence: `ai_docs/items/initiative-executor-roslyn-tool-discovery-brief.md`.
