# hooks-apply-matcher-subset-drift — Derive the apply-reminder hook matcher from the catalog

**row:** `hooks-apply-matcher-subset-drift` · **pri:** `Medium` · **size:** `M`


## Anchors

- `hooks/hooks.json`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs`

## Acceptance

- [ ] A test fails when an *_apply tool in ServerSurfaceCatalog is not matched by the apply-reminder matcher.
- [ ] hooks.json matcher covers every *_apply tool; changelog fragment and release sentinel handled.

## Evidence

wa1 counted 9 of 26 covered; HookConfigurationTests.cs:62-63 pins only the regex shape; copilot-instructions.md:69 says 'keep in sync' with no gate. (doc-audit 2026-11-17)

## Notes

PR 2172 made the apply reminder static (a command hook): it fires after every matched apply, including mid-chain ones, so the matcher drift now decides how often it fires.
