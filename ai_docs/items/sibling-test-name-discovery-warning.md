# sibling-test-name-discovery-warning — Surface partial sibling-name discovery

**row:** `sibling-test-name-discovery-warning` · **pri:** `Medium` · **size:** `M` · **deps:** `mcp-sampling-mrtr-migration,scaffolding-io-warning-detail-redaction`

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


## 2026-09-04 execution re-vet addendum

The direct S shape is insufficient. Discovery occurs before the first sampling request, but `InputRequiredException` prevents a terminal result and replay returns pending suggestion state before rescanning. The correct M implementation must persist an opaque incomplete-discovery code/flag through compound request state, reuse a truthful redacted `ScaffoldingReadFailurePolicy` message, and preserve one-leg enumeration.

Additional anchors: `src/RoslynMcp.Core/Models/IScaffoldingService.cs`, `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`, `src/RoslynMcp.Host.Stdio/Middleware/RequestStateCodec.cs`, and `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`.

Regression: one readable and one injected-failing sibling retain usable names; the terminal warning appears exactly once after MRTR replay, contains an opaque correlation ID, and excludes path, filename, exception type/message, count, source, and sentinel secret. Unexpected exceptions and cancellation propagate; replay performs no second scan.


> Path correction (2026-09-04): supersedes the two shorthand paths in the re-vet addendum. The live files are `src/RoslynMcp.Core/Services/IScaffoldingService.cs` and `src/RoslynMcp.Host.Stdio/ProtocolCompatibility/RequestStateCodec.cs`.
