# actionlint-fixture-inherited-override — Isolate actionlint fixture process configuration

**row:** `actionlint-fixture-inherited-override` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs:412`
- `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`
- `eng/verify-actionlint.ps1:15`

## Acceptance

- [ ] Fixture gate processes clear inherited ROSLYNMCP_ACTIONLINT_PATH unless the test explicitly supplies a value; preserve production override support.
- [ ] Real cache-corruption and forced-download regressions fail before the fix with a valid inherited override and pass afterward; retain explicit invalid-override refusal.
- [ ] Full local and hosted validation pass with the documented prestaged-binary configuration.

## Evidence

- At main83493b08, RunGateAsync forwards nullable environment directly into PwshScriptRunner, inheriting the parent override.
- Session full gate on PR1781 head5c91d362 with checksum-verified ROSLYNMCP_ACTIONLINT_PATH failed CachedBinaryHashMismatch and ForcedDownloadFailure: both unexpectedly ran the override successfully (exit0).
- Initial cold actionlint download returned HTTP504. The production validator explicitly supports a checksum-pinned override; this is a fixture isolation defect, not proof of a production validator failure.
