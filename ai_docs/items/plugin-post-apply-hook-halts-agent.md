# plugin-post-apply-hook-halts-agent — Stop the shipped post-apply hook from ending the agent turn

**row:** `plugin-post-apply-hook-halts-agent` · **pri:** `Medium` · **size:** `S`

## Anchors

- `hooks/hooks.json`

## Acceptance

- [ ] After `rename_apply`, `move_file_apply`, `create_file_apply`, `remove_dead_code_apply`, `move_type_to_file_apply`, `apply_composite`, `extract_interface_apply`, `extract_type_apply` and `bulk_replace_type_apply`, an agent that queues `compile_check` next (or in the same tool batch) keeps running.
- [x] Decide the mechanism: a non-blocking reminder, or drop the prompt hook and return a verification hint in the apply response. Record the choice in the changelog (shipped plugin behaviour). Done in PR 2172: `hooks/hooks.json` apply hook is a `type: command` hook printing `hookSpecificOutput.additionalContext`; the `server_info` prompt hook sets `continueOnBlock`; `changelog.d/plugin-post-apply-hook-halts-agent.md` records it.
- [ ] Verify in a real client session with a subagent running a three-apply chain.

Remaining after PR 2172: bullets 1 and 3 need the refreshed plugin. Run `/roslyn-mcp:update` on the installed plugin, then run the live three-apply chain with a subagent and confirm the turn continues.

## Evidence

- HOOK: 4/4 orchestrator turns ended after a hooked apply (once with compile_check queued in the same batch); three subagents running apply chains ended without reporting.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).

## Notes

PR https://github.com/darylmcd/Roslyn-Backed-MCP/pull/2172 merged at head 96629cae lineage (origin/main 0187324b): mechanism decided and shipped; pri lowered High -> Medium because only live verification (after /roslyn-mcp:update) remains; row stays open for that.

Correction to the note above: PR 2172 merge commit is 0187324b (merged 2026-11-20T23:21:04Z).
