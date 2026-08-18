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
Options, cheapest first:

1. Add a Linux leg to `ci.yml` `validate` (matrix over `windows-self-hosted` +
   `ubuntu-latest`), at minimum for the release-gate test classes.
2. Route the full suite to a matrix and keep the self-hosted runner for the
   longer Roslyn/workspace tests only.

Either way the acceptance check is: reintroduce a case-only path typo or a
width-sensitive assertion and confirm the PR gate goes red.

## Anchors

- `.github/workflows/ci.yml`
- `.github/workflows/publish-nuget.yml`
- `tests/RoslynMcp.Tests/TestInfrastructure/PowerShellOutputNormalizer.cs`
