# CI Policy

This document is the single canonical source for validation requirements and merge-gating expectations.

## Repository Evidence

- `.github/workflows/ci.yml` runs on pull requests, pushes to `main`, and manual dispatch.
- CI currently runs `./eng/verify-ai-docs.ps1`.
- CI currently runs `./eng/verify-release.ps1 -Configuration Release` (includes `dotnet test` with **XPlat Code Coverage** to `artifacts/coverage`, then **ReportGenerator** HTML summary under `artifacts/coverage/report`).
- CI currently runs `dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive`.
- `.github/workflows/codeql.yml` runs the **Analyze C#** CodeQL job on every pull request, push to `main`, weekly schedule, and manual dispatch so the check stays required and always completes. On pull requests, a path filter runs first: if the diff only touches documentation or other non-code paths, CodeQL skips `init`/build/analyze (the job still succeeds). Pull requests that run CodeQL use the `security-extended` query suite; pushes to `main` and scheduled runs use `security-and-quality`. SARIF upload to GitHub Code Scanning is disabled (`upload: never`).

## Local Validation

- For AI-doc changes, run `./eng/verify-ai-docs.ps1`.
- For release-impacting or code changes, run `./eng/verify-release.ps1`.
- `eng/verify-release.ps1` performs restore, build, test (with Cobertura coverage under `artifacts/coverage`), publish, and hash-manifest generation.

## Merge Gating Expectations

- Treat the documented CI workflow as the required merge gate.
- Do not declare work merge-ready while required CI is failing.
- If branch synchronization is required by repository protection settings, synchronize before merge.

## Ownership

- Branch, worktree, and pull-request workflow belongs to `ai_docs/workflow.md`.
- Runtime assumptions and execution constraints belong to `ai_docs/runtime.md`.
