# Initiative Executor Roslyn Tool Discovery Measurement

<!-- purpose: Decide whether to edit the initiative-executor brief after recommend_workflow shipped. -->
<!-- scope: in-repo -->

## Question

Do refactoring initiative executors still bypass Roslyn semantic first-hop tools
after `recommend_workflow` exists?

## Sources Checked

- `ai_docs/reports/20260521T043918Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`
- `src/RoslynMcp.Host.Stdio/Tools/WorkflowRecommendationTools.cs`
- `tests/RoslynMcp.Tests/WorkflowRecommendationToolsTests.cs`
- `.claude/agents/initiative-executor.md`

`recommend_workflow` landed in commit
`2def1918aa16c30b5ae67f45360b8bd7d2db44f1` on
2026-05-21 18:34:18 -0500. The available multi-session retro was generated at
2026-05-21T04:39:18Z, so its refactoring-subagent sample is pre-router
evidence, not post-router evidence.

## Measurement

The available pre-router sample covered five `initiative-executor` refactoring
subagents:

| Sample | Value |
|---|---:|
| Refactoring initiative-executor sessions | 5 |
| Sessions with no core semantic first-hop calls (`find_references`, `symbol_search`, `rename_preview`, move/extract previews) | 4 of 5 |
| Sessions dominated by generic `Read` / `Grep` / `Edit` / `Bash` | 5 of 5 |
| `workspace_reload` calls per sampled subagent | 4-7 |
| Generic `Read` calls per sampled subagent | 27-41 |
| Generic `Grep` calls per sampled subagent | 18-19 |
| Generic `Bash` calls per sampled subagent | 18-41 |
| Generic `Edit` calls per sampled subagent | 7 in the explicitly counted samples |

That confirms the original discovery problem before the router existed. It does
not prove the behavior persists after `recommend_workflow`.

## Decision

No-go for editing `.claude/agents/initiative-executor.md` in this session.

The current evidence is strong enough to explain why `recommend_workflow` was
added, but it is not a valid post-router sample. There are no implementation
changes to the executor brief to make from this row without inventing evidence.
Do not add the follow-on brief-injection row yet.

If a future refactoring-only retro samples post-`2def1918` sessions and still
finds semantic first-hop bypass or high reload churn, re-add a bounded backlog
row to inject a Roslyn-first stanza into `.claude/agents/initiative-executor.md`
and validate it with a controlled refactor rerun.
