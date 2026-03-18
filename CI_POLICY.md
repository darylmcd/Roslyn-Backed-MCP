# CI Policy

This document is the single canonical source for validation and merge-gating expectations.

## Required Validation For Merge-Ready Handoff

1. Build: `dotnet build RoslynMcp.slnx --nologo`
2. Test: `dotnet test RoslynMcp.slnx --nologo`
3. If release-impacting files changed, run: `./eng/verify-release.ps1`

## Change-Class Expectations

- Docs-only changes:
  - Ensure markdown links/paths are valid.
  - Ensure no stale references remain after renames/moves.
- Product-code changes:
  - Complete build and tests locally before handoff.
  - Resolve new diagnostics introduced by the change.
- Surface/contract changes:
  - Update docs and tests in the same branch.

## Merge Gating

- Respect repository branch protection and required checks.
- Branch must be synchronized with base branch before merge-ready handoff when required by protection rules.
- Do not bypass failing required checks.

## Canonical Ownership

- Git and PR workflow details live in `ai_docs/workflow.md`.
- Runtime/tool behavior details live in `ai_docs/runtime.md`.
- This file remains the sole merge-gating and validation policy source.
