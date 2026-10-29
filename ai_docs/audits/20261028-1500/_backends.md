# Backends — refactor-audit 20261028-1500

| Check | State | Replacement used |
|---|---|---|
| C# Roslyn workspace (`workspace_load` + analyzers) | skipped: out of focus | focus is `.github/** tests/** eng/**` Security/Contract/TestQuality; sweeps were `rg`, `ci-map`, `ci-durations`, `gitleaks`, reading. C# stack status: `degraded: no Tier 1 analyzer sweep` |
| Python (`python-analysis`) / TS (`typescript`) | n/a | no Python or TS product code in scope (`eng/intake-from-issues.py` is a script; `ruff` present, not needed for the focus) |
| gitleaks | present | whole-tree scan, in-scope hits triaged |
| trufflehog | present | not used (gitleaks plus strong regex judged enough for three small trees) |
| osv-scanner | absent | `eng/verify-nuget-audit.ps1` exists in repo; not run (Dependency not in focus) |
| actionlint, zizmor | absent | repo-owned pinned actionlint runs inside `eng/verify-actionlint.ps1` (not executed: audit is read-only and the gate downloads a binary); `ci-lens: actionlint absent`, `ci-lens: zizmor absent` |
| shellcheck, PSScriptAnalyzer | absent | none |
| `ci-map.mjs` | ran | 2 findings (both refuted, see `_dropped.md`) |
| `ci-durations.mjs` | ran | `ci.yml` pull_request 30 runs 40701005805..40803366211; `publish-nuget.yml` push 30 runs; 4 error findings, all out-of-focus |
| `test-audit.mjs` | no bash suite | exits 2: `scripts/_test` is not a directory; this repo has no `_test` suite |
| `gh` GET | present | collaborators, secrets (names), rulesets, environments |
