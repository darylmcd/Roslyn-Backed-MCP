# codex-hook-runtime-event-contract — Prove the installed Codex hook event contract

**row:** `codex-hook-runtime-event-contract` · **pri:** `Low` · **size:** `M`

## Anchors

- `.codex/hooks.json`
- `.codex/hooks/pre-publish-changelog.ps1`
- `tests/RoslynMcp.Tests/CodexChangelogHookTests.cs`

## Acceptance

- [ ] Add a bounded, credential-free integration probe against a supported Codex hook-host harness that records the real Windows shell and GitHub connector `PreToolUse` payload names/shapes without executing a publication mutation.
- [ ] Assert the installed/supported host discovers the repository hook, denies one harmless publication-shaped command when the verifier fails, and permits it when the verifier passes.
- [ ] Retire obsolete matcher aliases if the real event contract proves they are unnecessary; keep the authoritative release gate independent of this probe.
- [ ] One host-event truth table is skipped with an explicit unsupported-host reason rather than silently simulating success.

## Evidence

The script-level hook regression can synthesize `Bash` and `shell_command` payloads, but synthetic JSON does not prove which tool name or payload shape the installed Codex host emits. The authoritative verifier contains the risk; this row prevents the convenience hook from becoming decorative configuration.
