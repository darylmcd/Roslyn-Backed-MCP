# ci-actionlint-pinned-gate — Checksum-pinned repository-owned actionlint gate

**row:** `ci-actionlint-pinned-gate` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/verify-actionlint.ps1` (new)
- `justfile`
- `CI_POLICY.md (local-validation table + `just ci` composition sentence)`
- `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs` (new)

## Acceptance

- [ ] Install a checksum-pinned repository-owned `actionlint` and run it from `just ci`; no developer-global executable is required.
- [ ] A hash-verified cache hit runs fully offline and never touches the network; a cache miss hashes the downloaded archive before extraction and fails closed on mismatch; a cold cache with no network fails naming the pinned URL and expected hash.
- [ ] Regression proves the pin is present, a hash mismatch fails closed, a cache hit stays offline, and the recipe is wired into `just ci`.
- [ ] `CI_POLICY.md`'s `just ci` composition sentence and local-validation table stay accurate as a sixth child is added.

## Evidence

The repo has no workflow linter of any kind — `rg -i actionlint` over the tree returns hits only under `ai_docs/`, and the `just ci` aggregate at `justfile:79` has no YAML/expression gate, so a malformed `if:` or `${{ }}` expression is caught only after push.

## Context

Split from `ci-router-pure-decision-and-actionlint` (2026-09-02) — see the sibling `ci-router-pure-decision` for why (the combined row was at 4/4 Rule 3 with `CI_POLICY.md` excluded).

**Design already resolved by the cold deepener:** declare `$PinnedVersion` plus a `$PinnedArchiveHashes` ordered hashtable keyed by RID (`win-x64`/`linux-x64`/`linux-arm64`/`osx-arm64`) **at the top of the script itself**, deliberately not a sidecar JSON, so the pin costs no extra production file. Resolve `artifacts/tools/actionlint/<version>/actionlint[.exe]`; `artifacts/` is already ignored at `.gitignore:73`, so no ignore edit is needed. Hash with `Get-FileHash -Algorithm SHA256` before extraction, mirroring `eng/verify-release.ps1:457`. `ROSLYNMCP_ACTIONLINT_PATH` may point at a pre-staged binary but is still hash-verified.

**Repo convention (enforced):** the recipe must use the `pwsh -NoProfile -File ./eng/...` form required by `tests/RoslynMcp.Tests/JustfilePortabilityTests.cs:20-25`. Mirror the script-gate test pattern in `tests/RoslynMcp.Tests/BreakingVersionGateTests.cs` via `tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs`.

**Expect fallout on first run:** actionlint may report pre-existing findings in `.github/workflows/publish-nuget.yml`; fix or explicitly baseline them in the same PR or `just ci` reds immediately.

**Scheduling:** `justfile` is also touched by `tool-update-owned-process-shutdown` (different recipe — `ci:` at `:79` vs `tool-update` at `:106`). Conflict-graph edge, not a build-order dependency.
