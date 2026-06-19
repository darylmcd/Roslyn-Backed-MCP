# ci-vuln-audit-gating — make the CI vulnerability audit blocking

**row:** `ci-vuln-audit-gating` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/workflows/ci.yml:135-138` (`Audit packages for known vulnerabilities` step)

## Acceptance

- [ ] A known-vulnerable direct or transitive package fails the `validate` job. `dotnet package list --vulnerable --include-transitive` exits 0 regardless of findings, so parse its output (e.g. fail when it prints "has the following vulnerable packages") or switch to a gating mechanism.
- [ ] Doc-only PRs (which skip this step today) remain unaffected.

## Evidence

- 2026-06-19 CI review: the step runs but its exit code is always 0 — a new CVE in a transitive dependency would not block a merge. CodeQL (`Analyze`) covers code-level security but not dependency CVEs.

## Context

Adjacent minor: `publish-nuget.yml:36` calls `verify-release.ps1` without `-NoCoverage`, so the release run collects coverage (~60–90 s) that is then discarded — pass `-NoCoverage` there while touching the workflows.
