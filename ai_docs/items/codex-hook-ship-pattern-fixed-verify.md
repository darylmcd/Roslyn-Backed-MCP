# codex-hook-ship-pattern-fixed-verify — Cover ship.mjs subcommands in the Codex hook test

**row:** `codex-hook-ship-pattern-fixed-verify` · **pri:** `Low` · **size:** `S` · **verified:** `f903fb6c1b3f5b94d38b6a970e17796b58ed6c8e`


## Anchors

- `.codex/hooks/pre-publish-changelog.ps1`
- `tests/RoslynMcp.Tests/CodexChangelogHookTests.cs`

## Acceptance

- [ ] Tests cover run, preflight and staged-guard plus a non-ship node command that must not deny.

## Evidence

doc-audit 2026-11-17 changed the pattern and one test case only. (doc-audit 2026-11-17)
