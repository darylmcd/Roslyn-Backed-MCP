# docs-ci-sdk-download-dependency — Decouple the docs leg from SDK acquisition

**row:** `docs-ci-sdk-download-dependency` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml:156` — Every matrix leg, including the policy-docs leg, acquires latest 10.0.x through setup-dotnet before its release gates and tests.
- `CI_POLICY.md:10` (routing table, policy-only row) — Policy-only docs run one hosted Linux leg: every release gate plus the docs-tier consumer test classes.
- `eng/docs-tier-test-consumers.json` — The consumer class list the docs leg runs; `DocsTierTestConsumerContractTests` requires a reviewed classification for every test class with a detectable docs-tier signal.
- `eng/verify-release.ps1` — `-DocsTierConsumerTests` is the docs leg's entry point; it builds the solution and runs `dotnet test`, so it needs an SDK.
- [PR1836 corrected-head CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40225081974) — Both docs-linux legs fail SDK setup HTTP 409 before repository tests.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs` — Preserve the docs route and fail-closed routing contracts.

## Acceptance

- [ ] The docs-only CI leg does not depend on a .NET SDK download. Preserve the policy-docs route decided 2026-11-17 (`CI_POLICY.md` § Docs Route Test Selection): every non-test gate, every docs-tier consumer test class, the version/changelog checks and the required aggregate. Do not convert docs changes to zero-test evidence routing and do not drop a consumer class.
- [ ] Choose and document an acquisition-independent execution source compatible with public fork trust, reproducible test inputs and the repository runner policy. Do not add self-hosted runners or weaken code-bearing latest-SDK/exact-floor coverage.
- [ ] Reproduce unavailable SDK acquisition in a regression control; prove every required docs validation still executes and succeeds without fetching the SDK, skipping tests or permitting missing tools to pass.
- [ ] Obtain independent plan/implementation review and required exact-head hosted qualification. This row authorizes future design/remediation, not a workflow workaround during the current hold.

## Evidence

- Operator requested this row on 2026-10-25 after SDK 10.0.403 / runtime 10.0.14 origin HTTP 409 blocked unchanged docs and metadata heads.
- Main run 40224224815 reproduced the installation failure; direct runtime origin probe 2026-10-25T19:26:28.9255502Z returned 409, exit 0.
- SDK installation failure and Markdown contract test obligations are separate constraints. Keep both the requested download independence and the docs route's test obligation; surface any operator-level policy decision before implementation.
- 2026-11-17: an operator decision replaced the four complete-partition docs shards with one leg that runs every release gate and only the docs-tier consumer classes (16 classes, 143 cases when it landed). The test obligation this row must preserve is that consumer set, no longer the whole suite. The SDK dependency is unchanged: the one leg still runs setup-dotnet, a Release build and `dotnet test`.
