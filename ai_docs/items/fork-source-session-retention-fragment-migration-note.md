# fork-source-session-retention-fragment-migration-note — Add an explicit migration section to the fork source-session retention BREAKING fragment

**row:** `fork-source-session-retention-fragment-migration-note` · **pri:** `Low` · **size:** `S`

## Anchors

- `changelog.d/fork-source-session-retention.md`
- `docs/decisions/0029-fork-source-session-lifecycle.md`

## Acceptance

- [ ] The fragment states `Migration:` with the old pattern (synchronous disposal of `IWorkspaceForkApplyService` resolutions) and the new one (`await provider.DisposeAsync()` / `await using`), matching the other eight BREAKING fragments.
- [ ] `eng/verify-changelog-fragments.ps1` passes.

## Evidence

- Of the 9 `Changed — BREAKING` fragments, this is the only one without the word "migrat" (it describes the `await using` requirement but no labeled migration).
