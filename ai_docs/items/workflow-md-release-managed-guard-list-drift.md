# workflow-md-release-managed-guard-list-drift — guard doc disagrees with the guard script

## Anchors

- `ai_docs/workflow.md`
- `eng/guard-release-managed-files.ps1`

## Acceptance

- [ ] The `Release-managed file guard` table in `ai_docs/workflow.md` lists every path the script actually guards, including `.claude-plugin/server.json`.
- [ ] The follow-on sentence's count is corrected: `eng/verify-version-drift.ps1` reports "All 6 version files agree", so the doc's "five version-source locations" is wrong.
- [ ] Ideally the doc cites the script as canonical (or the script's list is generated/asserted against the doc) so the two cannot drift again.

## Evidence

Hit live while making an intentional ad-hoc edit to `.claude-plugin/server.json`:

- `eng/guard-release-managed-files.ps1` `$managedExact` contains TEN entries — `directory.build.props`, `manifest.json`, `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, `.claude-plugin/server.json`, `changelog.md`, `eng/verify-version-drift.ps1`, `hooks/hooks.json`, `eng/verify-skills-are-generic.ps1`, plus `BannedSymbols.txt` matched by basename.
- `ai_docs/workflow.md`'s table lists NINE and omits `.claude-plugin/server.json` entirely.
- The same section then says "Files 1, 3, 4, 5, and 6 are the five version-source locations enumerated by `eng/verify-version-drift.ps1`" — but that script prints "All 6 version files agree on 2.3.8". The sixth is the omitted `server.json`.

Impact is small but real: an agent reads the table, concludes `server.json` is freely editable, and gets a hard PreToolUse block with no matching row in the documented set — exactly what happened here.

## Context

Surfaced while adding `environmentVariables` to the packaged `.mcp/server.json` so registry/dnx installs configure `ROSLYNMCP_SANCTIONED_ROOTS` automatically.
