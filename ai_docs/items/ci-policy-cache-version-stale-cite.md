# ci-policy-cache-version-stale-cite — CI_POLICY.md cites a stale actions/cache version

**row:** `ci-policy-cache-version-stale-cite` · **pri:** `Low` · **size:** `S`

## Anchors

- `CI_POLICY.md:12` (`The NuGet-packages cache (`actions/cache@v4` against ~/.nuget/packages) …`)
- `.github/workflows/ci.yml:96` (`uses: actions/cache@v5`)

## Acceptance

- [ ] `CI_POLICY.md`'s cache cite matches the version actually pinned in `ci.yml` (`actions/cache@v5`), so the validation doc no longer describes a stale action version.

## Evidence

- Row-2 implementer finding (2026-06-20 top-n-remediation, `ci-vuln-gate-format-json-hardening`): `CI_POLICY.md:12` references `actions/cache@v4` while `ci.yml:96` uses `actions/cache@v5`. Pre-existing drift, unrelated to the vuln-gate change.

## Context

One-line doc cite fix. `verify-ai-docs.ps1` does not flag this (the cache key description is prose, not a tracked symbol), so it persisted. Trivial; bundle with the next CI-doc touch.
