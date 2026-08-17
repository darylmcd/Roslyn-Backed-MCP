# sibling-test-name-discovery-warning — Surface partial sibling-name discovery

**row:** `sibling-test-name-discovery-warning` · **pri:** `Low` · **size:** `S` · **deps:** `mcp-sampling-mrtr-migration,scaffolding-io-warning-detail-redaction`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs` — `CollectSiblingTestMethodNames` and sampling-warning composition.
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Return collected names plus one stable incomplete-discovery warning when a sibling test file cannot be read for an expected IO/authorization reason; do not silently `continue`.
- [ ] Append that warning to the sampling result while retaining usable names and the deterministic placeholder fallback.
- [ ] Exclude exception type, raw message, full path, and secret-bearing values by reusing the scaffolding warning policy; unexpected exceptions and cancellation are not swallowed.
- [ ] One deterministic per-file read-failure regression proves the warning is emitted once and a successful sibling file still contributes its method names.

## Evidence

- `CollectSiblingTestMethodNames` catches `IOException` and `UnauthorizedAccessException` around each `File.ReadAllText` and continues without any result or diagnostic signal.
- The partial list feeds sampled test-name selection, so the silent omission can hide a collision while the rest of scaffolding succeeds.
