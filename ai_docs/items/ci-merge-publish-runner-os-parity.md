## Problem

The merge gate and the publish gate run on different operating systems, so a test
that passes on one can block the other:

| Gate | Workflow | Runner |
|---|---|---|
| merge | `ci.yml` `validate` | self-hosted **Windows** |
| publish | `publish-nuget.yml` | GitHub-hosted **Linux** |

A PR can therefore go green, merge, and tag — and only then fail at publish. This
happened on the v3.0.1 cut (2026-08-18): three tests added after v3.0.0 had never
run on Linux, all three failed in `publish-nuget`, and the tag shipped no package.

Root causes found in that incident (all fixed in
`fix/cross-platform-release-test-assertions`):

- `NuGetAuditGateTests` read `"Justfile"`; the on-disk name is `justfile`. Resolves
  on case-insensitive Windows, throws on Linux.
- `BreakingVersionGateTests` and `VerifyReleaseChildScriptTests` asserted substrings
  against raw PowerShell `Write-Error` output, which hard-wraps at console width and
  prefixes continuation lines with a `|` gutter. The wrap column is host-dependent.

## Fix

Make the pre-merge gate exercise Linux so this class fails before the tag, not after.

**Decision (2026-08-19): option 2 — run the FULL suite on a `ubuntu-latest` leg.**
The two options originally recorded here were:

1. Linux leg covering *at minimum the release-gate test classes*.
2. Full suite on a matrix, self-hosted retained for the longer Roslyn/workspace work.

Option 1 is rejected because it does not close the class. `publish-nuget.yml:44`
runs `./eng/verify-release.ps1` — the **entire** suite — on `ubuntu-latest`. The
pre-merge gate's Linux coverage must therefore match the publish gate's, or the
same failure mode simply moves to whichever test is OS-sensitive next. The v3.0.1
incident happened to land in the release-gate classes; nothing about the exposure
is specific to them. Narrowing to those classes would have caught that incident
and not the next one.

Cost note: the Linux leg is GitHub-hosted, so it runs concurrently with the
self-hosted Windows leg rather than queueing behind it (the self-hosted runner
processes one validate job at a time). Local full-suite wall clock is ~11 min.
If CI minutes later prove to be the binding constraint, downgrading to option 1
is a deliberate, recorded trade — not a silent narrowing.

## Acceptance

- [ ] `ci.yml` `validate` runs on a matrix that includes `ubuntu-latest` alongside the routed self-hosted Windows runner, and the Linux leg executes the same `eng/verify-release.ps1` entry point the publish gate uses (`publish-nuget.yml:44`) — not a narrowed test filter.
- [ ] The Linux leg is a required check for merge, so a Linux-only failure blocks the PR rather than surfacing at tag time.
- [ ] **Regression proof:** reintroduce a case-only path typo (e.g. `"Justfile"` for on-disk `justfile`) on a scratch branch and confirm the PR gate goes red on the Linux leg while the Windows leg stays green. Do the same for a width-sensitive raw-`Write-Error` substring assertion (the shape `PowerShellOutputNormalizer` exists to neutralize).
- [ ] The existing self-hosted PR path keeps its `-ExcludeNetworkTests` treatment (`ci.yml:239`); the Linux leg's flag set is stated explicitly rather than inherited by accident.
- [ ] `docs/release-policy.md` (or `ai_docs/workflow.md`, whichever documents the gate topology) records that merge and publish now share OS coverage, so the next reader does not have to re-derive it from two workflow files.

## Anchors

- `.github/workflows/ci.yml`
- `.github/workflows/publish-nuget.yml`
- `tests/RoslynMcp.Tests/TestInfrastructure/PowerShellOutputNormalizer.cs`
