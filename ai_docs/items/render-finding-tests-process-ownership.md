# render-finding-tests-process-ownership — Use owned bounded processes in renderer script tests

**row:** `render-finding-tests-process-ownership` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Skills/RenderFindingScriptTests.cs:301`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`
- `skills/mcp-server-surface-test/lib/render-finding.ps1:47`

## Acceptance

- [ ] Observe a regression for stderr pressure, bounded deadline and owned process completion on the old private snippet/Git runners, then adopt the existing shared runner for every same-mechanism caller.
- [ ] Preserve all snippets, stdout/stderr/exit assertions, argument boundaries and existing60s/30s budgets. Concurrently drain streams and await completion/reap; do not leave descendants or unobserved output tasks.
- [ ] Correct stale in-process invocation wording where appropriate; this process-lifetime repair must not be presented as the cure for exit139 without separate causal evidence.

## Evidence

- Private RunPwshSnippet reads stdout then stderr synchronously before WaitForExit(60000), so it cannot enforce its deadline under pipe pressure; kill throws before reap/drain. RunGit ignores its timed-wait result and reads stderr only after failure. Separate pre-existing mechanism; not established cause of the2s139.
