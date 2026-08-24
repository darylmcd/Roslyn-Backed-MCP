# modelcontextprotocol-license-attribution-correction — Correct MCP SDK legal attribution

**row:** `modelcontextprotocol-license-attribution-correction` · **pri:** `High` · **size:** `M`

## Anchors

- `eng/update-third-party-notices.ps1`
- `THIRD-PARTY-NOTICES.md`
- `tests/RoslynMcp.Tests/ThirdPartyNoticeDriftTests.cs`

## Acceptance

- [ ] Verify the restored `ModelContextProtocol` package license from its authoritative NuGet metadata for the currently pinned and proposed SDK versions.
- [ ] Correct both the notice generator catalog and checked-in notice; regenerated notices remain deterministic.
- [ ] Add one regression that compares the declared MCP SDK attribution with restored package metadata so a future license change cannot be hidden by matching stale generated output.
- [ ] Run the legal-notice verifier and complete release gate before publishing another package.

## Evidence

PR #1326 review found that the restored ModelContextProtocol 2.1.0 and 2.2.0 package nuspec metadata declares Apache-2.0 while `eng/update-third-party-notices.ps1` and `THIRD-PARTY-NOTICES.md` declare MIT. The current drift test only proves generated output matches the hardcoded catalog, so both can agree while attribution is wrong.
