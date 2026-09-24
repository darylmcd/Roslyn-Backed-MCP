# surface-test-skill-prompt-drift — Fix drift in the shipped mcp-server-surface-test skill

**row:** `surface-test-skill-prompt-drift` · **pri:** `Medium` · **size:** `S`

## Anchors

- `skills/mcp-server-surface-test/SKILL.md:19`
- `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md:88`
- `skills/mcp-server-surface-test/prompts/phases/setup-and-analysis.md:32`
- `.claude-plugin/plugin.json`

## Acceptance

- [ ] Step 1 / Phase -1 gate accepts connection.state=idle before the first workspace_load (server_heartbeat documents idle→ready only after a load)
- [ ] 6k.4 names the apply tool symbol_refactor_preview tokens actually redeem with
- [ ] Phase 8 step 10b describes get_test_coverage_map as the test_coverage alias it is
- [ ] --full either ships agents/audit-phase-runner.md in the plugin (plugin.json agents) or the dispatch plan names a generic agent type

## Evidence

- 2026-09-24 run: server_info before load → connection.state=idle (gate says must be ready); G4: symbol_refactor token → preview_multi_file_edit_apply 'workspace was reloaded' vs prompt :88; G5 #25: get_test_coverage_map is an alias of test_coverage; plugin cache 4.2.1 has no agents/ dir and plugin.json agents=null while the repo has agents/audit-phase-runner.md. — see `ai_docs/audits/20260924-1305/report.md` (check C3) and `ai_docs/audits/20260924-1305/findings.json`
