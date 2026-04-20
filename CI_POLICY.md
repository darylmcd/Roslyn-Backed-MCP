# CI Policy

This document is the single canonical source for validation requirements and merge-gating expectations.

## Repository Evidence

- `.github/workflows/ci.yml` runs on pull requests, pushes to `main`, and manual dispatch.
- CI currently runs `./eng/verify-ai-docs.ps1`. This script enforces AI-doc stale-reference and broken-link checks and also invokes `./eng/verify-skills-are-generic.ps1`, which blocks a PR whose `./skills/**/SKILL.md` files reference repo-only markers (`ai_docs/`, `backlog.md`, `state.json`, `backlog-sweep`, `schemaVersion`, `eng/`, `just verify-`, `Directory.Build.props`, `BannedSymbols.txt`). The skills-generic check runs unconditionally on every PR, including docs-only PRs where `verify-release.ps1` is skipped, so shipped-plugin surface validation is never bypassed.
- CI currently runs `./eng/verify-release.ps1 -Configuration Release`. Coverage collection is gated on event type: pull requests run with `-NoCoverage` (skips `dotnet test --collect:"XPlat Code Coverage"`, the ReportGenerator HTML summary step, and the `code-coverage` artifact upload — coverage is informational, not a merge gate, and the coverlet IL-rewrite cost is ~60–90s per PR). Pushes to `main` and manual dispatches (`workflow_dispatch`) run full coverage collection with **XPlat Code Coverage** to `artifacts/coverage`, then **ReportGenerator** HTML summary under `artifacts/coverage/report`, then upload the `code-coverage` artifact so trend data continues on main. The same gate covers any future non-PR triggers (e.g. `schedule:`) via `github.event_name != 'pull_request'`.
- CI's `detect-docs-only` step (pull requests only) matches the regex `^(.*\.md|ai_docs/.*\.json)$` against every changed file. When every file in the diff matches, `verify-release.ps1`, the NuGet vulnerability audit, and both artifact uploads are skipped — saving ~10 minutes of wall time per doc-only PR. Because `.*\.md` has no directory anchor, the pattern covers **every** `.md` path in the tree: top-level (`README.md`, `CHANGELOG.md`, `CI_POLICY.md`, `CLAUDE.md`, `AGENTS.md`), `ai_docs/**/*.md`, `docs/**/*.md`, `skills/**/*.md` (shipped plugin surface), `.claude/**/*.md` (gitignored — never appears in PR diffs, listed for completeness), `.github/**/*.md`, and any future `**/*.md` additions. The `ai_docs/.*\.json` alternative additionally covers plan-state and reconcile-state JSON files under `ai_docs/plans/` and `ai_docs/reports/`, which are orchestrator state files never consumed by the build. `verify-ai-docs.ps1` always runs on PRs regardless of the gate, so documentation stale-reference, broken-link, and shipped-skill-genericity validation still gate doc-only PRs. Pushes to `main` and `workflow_dispatch` always run the full pipeline regardless of file changes.
- CI currently runs `dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive`.
- `.github/workflows/codeql.yml` runs the **Analyze C#** CodeQL job on every pull request, push to `main`, weekly schedule, and manual dispatch so the check stays required and always completes. On pull requests, a path filter runs first: if the diff only touches documentation or other non-code paths, CodeQL skips `init`/build/analyze (the job still succeeds). Pull requests that run CodeQL use the `security-extended` query suite; pushes to `main` and scheduled runs use `security-and-quality`. SARIF upload to GitHub Code Scanning is disabled (`upload: never`).

## Local Validation

- For AI-doc changes, run `./eng/verify-ai-docs.ps1`.
- For release-impacting or code changes, run `./eng/verify-release.ps1`.
- `eng/verify-release.ps1` performs restore, build, test (with Cobertura coverage under `artifacts/coverage`), publish, and hash-manifest generation. Pass `-NoCoverage` to skip the `--collect:"XPlat Code Coverage"` IL-rewrite step for faster iteration when you don't need the coverage artifact (matches the CI pull-request path).

## Merge Gating Expectations

- Treat the documented CI workflow as the required merge gate.
- Do not declare work merge-ready while required CI is failing.
- If branch synchronization is required by repository protection settings, synchronize before merge.

## Ownership

- Branch, worktree, and pull-request workflow belongs to `ai_docs/workflow.md`.
- Runtime assumptions and execution constraints belong to `ai_docs/runtime.md`.
