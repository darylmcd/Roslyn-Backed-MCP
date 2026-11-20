# plugin-post-apply-hook-halts-agent — Stop the shipped post-apply hook from ending the agent turn

**row:** `plugin-post-apply-hook-halts-agent` · **pri:** `High` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `hooks/hooks.json`

## Acceptance

- [ ] After `rename_apply`, `move_file_apply`, `create_file_apply`, `remove_dead_code_apply`, `move_type_to_file_apply`, `apply_composite`, `extract_interface_apply`, `extract_type_apply` and `bulk_replace_type_apply`, an agent that queues `compile_check` next (or in the same tool batch) keeps running.
- [ ] Decide the mechanism: a non-blocking reminder, or drop the prompt hook and return a verification hint in the apply response. Record the choice in the changelog (shipped plugin behaviour).
- [ ] Verify in a real client session with a subagent running a three-apply chain.

## Evidence

- HOOK: 4/4 orchestrator turns ended after a hooked apply (once with compile_check queued in the same batch); three subagents running apply chains ended without reporting.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
