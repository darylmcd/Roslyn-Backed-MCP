# CI Policy

This document is the single canonical source for validation requirements and merge-gating expectations.

## Repository Evidence

- `.github/workflows/ci.yml` runs on pull requests, manual dispatch, and a weekly schedule (Mon 05:45 UTC). Push-to-`main` is intentionally not a trigger: the default-branch ruleset requires merges to arrive via PR, so every commit on `main` was validated seconds earlier on its PR head. Coverage trend data is kept fresh by the weekly `schedule` run instead of per-merge collection. The `validate` job sets `timeout-minutes: 25` so a hung test does not burn the 6-hour default. A `concurrency:` group cancels superseded PR runs (`cancel-in-progress` is gated on `github.event_name == 'pull_request'` so dispatch / schedule runs never cancel).
- CI currently runs `./eng/verify-ai-docs.ps1`. This script enforces AI-doc stale-reference and broken-link checks and also invokes `./eng/verify-skills-are-generic.ps1`, which blocks a PR whose `./skills/**/SKILL.md` files reference repo-only markers (`ai_docs/`, `backlog.md`, `state.json`, `backlog-sweep`, `schemaVersion`, `eng/`, `just verify-`, `Directory.Build.props`, `BannedSymbols.txt`). The skills-generic check runs unconditionally on every PR, including docs-only PRs where `verify-release.ps1` is skipped, so shipped-plugin surface validation is never bypassed.
- CI currently runs `./eng/verify-release.ps1 -Configuration Release`. The script applies `--filter "TestCategory!=Benchmark"` to the `dotnet test` invocation so the opt-in `WorkspaceReadConcurrencyBenchmark` stays out of the default run (run it explicitly via `dotnet test --filter "TestCategory=Benchmark"`). Coverage collection is gated on event type: pull requests run with `-NoCoverage` (skips `dotnet test --collect:"XPlat Code Coverage"`, the ReportGenerator HTML summary step, and the `code-coverage` artifact upload — coverage is informational, not a merge gate, and the coverlet IL-rewrite cost is ~60–90s per PR). Manual dispatches (`workflow_dispatch`) and the weekly schedule run full coverage collection with **XPlat Code Coverage** to `artifacts/coverage`, then **ReportGenerator** HTML summary under `artifacts/coverage/report`, then upload the `code-coverage` artifact. The gate is `github.event_name != 'pull_request'`.
- CI's `detect-docs-only` step (pull requests only) matches the regex `^(.*\.md|ai_docs/.*\.json)$` against every changed file. When every file in the diff matches, `verify-release.ps1`, the NuGet vulnerability audit, and both artifact uploads are skipped — saving ~12 minutes of wall time per doc-only PR. Because `.*\.md` has no directory anchor, the pattern covers **every** `.md` path in the tree: top-level (`README.md`, `CHANGELOG.md`, `CI_POLICY.md`, `CLAUDE.md`, `AGENTS.md`), `ai_docs/**/*.md`, `docs/**/*.md`, `skills/**/*.md` (shipped plugin surface), `.claude/**/*.md` (gitignored — never appears in PR diffs, listed for completeness), `.github/**/*.md`, and any future `**/*.md` additions. The `ai_docs/.*\.json` alternative additionally covers plan-state and reconcile-state JSON files under `ai_docs/plans/` and `ai_docs/reports/`, which are orchestrator state files never consumed by the build. `verify-ai-docs.ps1` always runs on PRs regardless of the gate, so documentation stale-reference, broken-link, and shipped-skill-genericity validation still gate doc-only PRs. `workflow_dispatch` and the weekly `schedule` run the full pipeline regardless of file changes.
- The NuGet-packages cache (`actions/cache@v4` against `~/.nuget/packages`) keys on `Directory.Packages.props`, `Directory.Build.props`, and `global.json`. Project-file edits no longer invalidate the cache because the package graph is owned by the central-version pin, not individual csproj files — this keeps the repo under the 10 GB Actions-cache cap.
- Published artifacts (`host-stdio-publish`, `release-manifests`) use a 14-day retention; the `code-coverage` artifact uses 30-day retention. Both are set explicitly on the upload step so the default 90-day retention does not silently consume the Actions storage quota.
- CI currently runs `dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive`.
- `.github/workflows/codeql.yml` runs the **Analyze C#** CodeQL job on every pull request, a weekly `schedule` (Mon 05:27 UTC), and via `workflow_dispatch`. Push-to-`main` is not a trigger (the squash-merged commit was analyzed seconds earlier on its PR). On pull requests, a path filter runs first: if the diff only touches documentation or other non-code paths, CodeQL skips `init`/build/analyze (the job still succeeds). Pull requests that run CodeQL use the `security-extended` query suite; scheduled runs use `security-and-quality`. SARIF results upload to the repository Security tab via the default `upload: always` behavior — the workflow declares `permissions: security-events: write` for this.

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
