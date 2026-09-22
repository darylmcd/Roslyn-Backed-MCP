# docs-only-allowlist-throws-instead-of-fallback — Docs-only allowlist fail-closed path throws instead of falling back to full suite

**row:** `docs-only-allowlist-throws-instead-of-fallback` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-release.ps1:404-420`

## Acceptance

- [ ] A missing, empty, or unknown-class `eng/docs-only-test-classes.txt` allowlist logs a warning and runs the unsharded full suite on shard 0, instead of throwing (hard CI failure).
- [ ] This matches `docs-only-route-runs-full-test-suite`'s own acceptance text verbatim: "anything the logic cannot classify runs the full suite, so a missed class costs time, never coverage."
- [ ] Add/extend a regression test covering the fallback path (missing file, empty file, and an unknown class name), distinct from the existing `DocsOnlyAllowlist_IsSortedExactAndEveryClassExists` contract test which only guards renames through the full-topology route.

## Evidence

Cold review of `/backlog-remediate` run `20260921T211855Z_backlog-remediate` (PR #1573, commit `d8a3b521`) found the implementation throws instead of falling back:

```powershell
if (-not (Test-Path -LiteralPath $allowlistPath -PathType Leaf)) {
    throw "Docs-only test allowlist is missing: $allowlistPath"
}
...
if ($unknownClasses.Count -gt 0) {
    throw "Docs-only test allowlist names classes absent from the test assembly: $($unknownClasses -join ', ')"
}
```

The only genuine "fall back to full suite" path is `$DocsOnlyTestSelection -and $TestShardCount -le 1`, which is dead in normal CI operation (the docs-only legs are hardcoded to `TestShardCount=2` in `.github/workflows/ci.yml`).

Mostly defanged in practice: `CiTopologyDecisionContractTests.DocsOnlyAllowlist_IsSortedExactAndEveryClassExists` runs in the full suite, so a PR renaming an allowlisted class is itself a code change and routes to the full topology before merge, never hitting this throw. The real gap is a docs-only PR hand-editing `eng/docs-only-test-classes.txt` itself (e.g. a typo'd class name) — that gets a hard-blocked merge instead of the promised full-suite fallback.
