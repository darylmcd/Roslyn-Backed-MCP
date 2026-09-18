# docs-only-route-runs-full-test-suite — docs-only-route-runs-full-test-suite

**row:** `docs-only-route-runs-full-test-suite` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/verify-release.ps1` — narrow the class filter when the route is docs-only.
- `.github/workflows/ci.yml` — pass the docs-only selection into the validate step.
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs` — pin the selection contract.

## Acceptance

- [ ] On the docs-only route, the validate legs run a declared documentation-contract test set rather than the full sharded suite.
- [ ] The selection is an explicit allowlist and fails closed: anything the logic cannot classify runs the full suite, so a missed class costs time, never coverage.
- [ ] The full-route topology, its four Windows and two Linux legs, and the existing `TestCategory!=Benchmark` exclusion are unchanged.
- [ ] A contract test pins which classes the docs-only route runs, so adding a documentation-contract class without registering it fails loudly.

## Evidence

- Measured 2026-09-18 on PR #1545 (a three-row `ai_docs/backlog.md` change plus three new `ai_docs/items/*.md` files): the `ci` workflow ran 941s. It routed docs-only correctly — two `docs-linux` legs, no Windows legs.
- `.github/workflows/ci.yml:143-145` sets `TestShardOnly = $true` for the docs-only route. `eng/verify-release.ps1:252,270,326,442` shows that switch skips packaging, formatting, the SDK-floor probe and artifact work — but not the test run itself, which still executes at line 428 with the shard's class filter.
- `tests/RoslynMcp.Tests/` holds 343 test classes. The documentation- and CI-contract subset is roughly two dozen (`CiTopologyDecisionContractTests`, `PluginInstallDocumentationContractTests`, `ReleaseManagedFileGuardDocumentationTests`, `PublishWorkflowContractTests`, `ActionlintGateContractTests`, `CiRunnerParityContractTests`, and similar).
- `eng/verify-release.ps1:348-350,428` already builds and passes a `--filter` expression, so the mechanism to narrow selection exists; only the docs-only input to it is missing.

## Context

The routing half of this is already correct and should not be touched. `eng/resolve-ci-topology.ps1:95-99` deliberately excludes behavior-bearing markdown — `CHANGELOG.md` and anything under `skills/`, `.claude/skills/`, `agents/`, `.claude/agents/`, `.github/prompts/` — from the docs-only classification, because in a published plugin those markdown files are the shipped product. That exclusion is the reason "any `.md` is not a code change" is false for this repo, and it stays.

The gap is narrower: docs-only narrows the *topology* but not the *test selection*, so a one-line backlog edit still runs every test class the shard plan assigns it.

Main risk, and why the fail-closed bullet is load-bearing: under-selecting silently drops coverage on a documentation contract that a doc edit really can break. An allowlist that defaults to the full suite converts that failure mode from lost coverage into lost time.

Scope guard: this is a test-selection change at one call site plus its contract test. Not a CI redesign, and not a change to how routes are decided.
Anchor-overlap check 2026-09-18 — five rows share .github/workflows/ci.yml or eng/verify-release.ps1; none duplicates this row, but three interact. (1) coverage-baseline-stale re-measures coverage by running verify-release.ps1: the narrowed selection must not apply to that measurement path, which still needs the full suite. (2) ci-merge-gate-publish-gate-shape-divergence adds a scheduled unsharded full leg — complementary, and it strengthens the case for narrowing PR-time selection since full coverage would then have a scheduled home. (3) resolve-ci-topology-enumeration-failed-unreachable reworks the resolver fail-closed path this row depends on for its own fail-closed bullet; sequence after it if both are in flight. ci.yml is a five-row hotspot — check for in-flight edits before starting.
