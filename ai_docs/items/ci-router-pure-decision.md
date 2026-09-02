# ci-router-pure-decision — Extract the CI topology into an executable pure decision

**row:** `ci-router-pure-decision` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.github/workflows/ci.yml`
- `eng/resolve-ci-topology.ps1` (new)
- `CI_POLICY.md` (router prose)
- `tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs` (new)
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Extract a pure topology decision taking event kind, changed-file records and the reported file count, emitting `{docs_only, runner_matrix, reason}`; GitHub API access stays at the workflow boundary.
- [ ] Table-test code, policy docs, current/original rename paths, behavior-bearing Markdown, count mismatch/cap, API failure, pull request, dispatch and schedule inputs.
- [ ] Assert the exact leg set, complete shard union, sole artifact owner, and event-specific aggregate check name for every route.
- [ ] Keep only workflow integration sentinels in `CiRunnerParityContractTests`; comments and unrelated substrings cannot satisfy routing behavior.

## Evidence

The routing decision is 113 lines of PowerShell inlined into one workflow step at `.github/workflows/ci.yml:40-152` (leg construction `:43-91`, docs-only classification `:124-138`, emission `:144-152`). It cannot be executed outside GitHub Actions because `${{ }}` expressions are textually interpolated into the script body at `:93`, `:96`, `:100`, `:107` — so the fail-closed count-mismatch/cap branch at `:131` and the rename-origin union at `:114` have never run against a real input. Consequently 48 of the 107 assertions in `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs:41-189` are substring sentinels over PowerShell source, not behavior.

## Context

Split from `ci-router-pure-decision-and-actionlint` (2026-09-02), on the cold plan-deepener's own recommendation: the combined row sat at **4/4 on Rule 3 and 3/3 on Rule 4** with a genuine 5th production file (`CI_POLICY.md`) excluded to hold the cap. Splitting lets each half land its own doc-sync legally.

**Verified not gate-forced:** `eng/verify-ai-docs.ps1:2-12` scopes to `ai_docs/`, and the only test naming `CI_POLICY.md` (`tests/RoslynMcp.Tests/Skills/ChangedFormatGateScriptTests.cs:94`) uses it in an assertion *message*, not an assertion. So no Rule 3 exemption class covered it — a split was the correct fix, not a fabricated gate citation.

**Hard constraint:** the router must keep emitting byte-identical JSON to today (`ConvertTo-Json -Compress -Depth 6 -AsArray` at `ci.yml:71`) or `fromJSON(needs.route.outputs.runner_matrix)` at `:160` breaks the matrix. Repro that the extraction is behavior-preserving by invoking the new script with a captured real files-API payload and diffing `runner_matrix` byte-for-byte against the current inline step's output.

**Largest risk of silent contract loss:** the 48 route-bound assertions across 4 of 11 test methods must be RELOCATED as behavioral table cases, not dropped.

**Fail-closed property to preserve:** the docs-only ordering guard at `CiRunnerParityContractTests.cs:166-174` (complete enumeration must precede the docs decision) is real behavior, not a text artifact.
