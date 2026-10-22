# test-generator-fixture-parallel-ownership — Verify private generator fixtures can leave the serialized tail

**row:** `test-generator-fixture-parallel-ownership` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/DiagnosticSourceGeneratorParityTests.cs:6`
- `tests/RoslynMcp.Tests/GeneratorProjectSymbolIdentityTests.cs:15`

## Acceptance

- [ ] Trace MSBuild/NuGet/analyzer and process lifetimes; either remove global contention through explicit ownership or document a concrete unavoidable shared dependency.
- [ ] Retain generator/build parity and symbol-identity assertions and real restore/build behavior.
- [ ] Remove DoNotParallelize only with repeated concurrent Windows/Linux evidence; do not loosen timing assertions or classify failures as flakes.

## Evidence

- Both opt-out comments cite generic MSBuild/NuGet contention despite class-private fixture roots; CI policy requires a concrete process-global/shared-workspace dependency. Optimization candidate, not a proven safe attribute deletion.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
