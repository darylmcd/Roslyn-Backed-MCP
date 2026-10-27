# docs-ci-sdk-download-dependency — Decouple docs legs from SDK acquisition

**row:** `docs-ci-sdk-download-dependency` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml:142` — All matrix legs, including docs-linux, acquire latest10.0.x through setup-dotnet before their test execution.
- `CI_POLICY.md:10` — Policy-only docs use three hosted Linux class shards; Markdown contract consumers require complete test coverage.
- [PR1836 corrected-head CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40225081974) — Both docs-linux legs fail SDK setupHTTP409 before repository tests.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs` — Preserve docs partition and fail-closed routing contracts.

## Acceptance

- [ ] Docs-only CI legs do not depend on a .NET SDK download. Preserve the complete two-shard documentation contract test partition, version/changelog checks and required aggregate; do not convert docs changes to zero-test evidence routing.
- [ ] Choose and document an acquisition-independent execution source compatible with public fork trust, reproducible test inputs and the repository runner policy. Do not add self-hosted runners or weaken code-bearing latest-SDK/exact-floor coverage.
- [ ] Reproduce unavailable SDK acquisition in a regression control; prove every required docs validation still executes and succeeds without fetching the SDK, skipping tests or permitting missing tools to pass.
- [ ] Obtain independent plan/implementation review and required exact-head hosted qualification. This row authorizes future design/remediation, not a workflow workaround during the current hold.

## Evidence

- Operator requested this row on2026-10-25 after SDK10.0.403/runtime10.0.14 originHTTP409 blocked unchanged docs and metadata heads.
- Current main run40224224815 reproduces installation failure; direct runtime origin probe2026-10-25T19:26:28.9255502Z returns409, exit0.
- SDK installation failure and Markdown contract test obligations are separate constraints. Preserve both the requested download independence and current complete test coverage; surface any operator-level policy decision before implementation.
