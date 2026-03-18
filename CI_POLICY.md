# CI Policy

This document is the single canonical source for validation requirements and merge-gating expectations.

## Repository Evidence

- `.github/workflows/ci.yml` runs on pull requests, pushes to `main`, and manual dispatch.
- CI currently runs `./eng/verify-ai-docs.ps1`.
- CI currently runs `./eng/verify-release.ps1 -Configuration Release`.
- CI currently runs `dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive`.

## Local Validation

- For AI-doc changes, run `./eng/verify-ai-docs.ps1`.
- For release-impacting or code changes, run `./eng/verify-release.ps1`.
- `eng/verify-release.ps1` performs restore, build, test, publish, and hash-manifest generation.

## Merge Gating Expectations

- Treat the documented CI workflow as the required merge gate.
- Do not declare work merge-ready while required CI is failing.
- If branch synchronization is required by repository protection settings, synchronize before merge.

## Ownership

- Branch, worktree, and pull-request workflow belongs to `ai_docs/workflow.md`.
- Runtime assumptions and execution constraints belong to `ai_docs/runtime.md`.
