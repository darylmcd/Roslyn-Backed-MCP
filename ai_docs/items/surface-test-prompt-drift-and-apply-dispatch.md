# surface-test-prompt-drift-and-apply-dispatch — Correct the surface-test prompt where it disagrees with the server

**row:** `surface-test-prompt-drift-and-apply-dispatch` · **pri:** `Low` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `skills/mcp-server-surface-test/prompts/phases/setup-and-analysis.md`
- `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`

## Acceptance

- [ ] `nonExhaustiveSwitches` → `switchExhaustivenessIssues`; trace_exception_flow described by exception type; change-signature rename tokens via rename_apply; scaffold batch apply route matches the tool.
- [ ] The dispatch table no longer lists Phase 6 as a runner group (the runner agent refuses it) and says where apply chains run.
- [ ] `just verify-skills` passes.

## Evidence

- Section 7 (prompt drift) and the dispatch note in the report.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
