# changelog-process-tests-runner-adoption — Move changelog process tests onto the shared runner

**row:** `changelog-process-tests-runner-adoption` · **pri:** `Low` · **size:** `S` · **deps:** `powershell-script-test-runner-foundation,codex-hook-runtime-event-contract`

## Anchors

- `tests/RoslynMcp.Tests/ChangelogFragmentRequirementTests.cs`
- `tests/RoslynMcp.Tests/CodexChangelogHookTests.cs`

## Acceptance

- [ ] Replace both private PowerShell `ProcessStartInfo`/stream/timeout/result implementations with `PwshScriptRunner` after the foundation row lands.
- [ ] Preserve stdin delivery for hook JSON, argument-list safety for fixture paths, concurrent stdout/stderr drain, bounded tree-kill behavior, and existing diagnostics.
- [ ] Keep the full git-state, grammar, hook-boundary, malformed-input, and missing-verifier matrices unchanged.
- [ ] One before/after contract assertion proves both suites use the shared runner and no private `ProcessResult`/pwsh resolver remains.

## Evidence

The new changelog verifier and hook suites each need process isolation, but currently repeat the same pwsh resolution, redirected stream, timeout, kill, and immutable-result machinery already tracked by `powershell-script-test-runner-foundation`. Folding them into that foundation would exceed its bounded test-file scope.
