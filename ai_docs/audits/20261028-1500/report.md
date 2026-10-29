# Refactor audit — 20261028-1500

## Health summary

Focus `TestQuality, Contract, Security` over `.github/** tests/** eng/**`, commit f8734a2f (v5.0.0), depth deep, backlog apply, fan-out unavailable by choice (all inline; no High/Critical candidate, so no refutation subagents; budget 0). Four findings survive: 2 Medium, 2 Low, none High. Backends: `ci-map`, `ci-durations`, `gitleaks`, `gh` GETs ran; `test-audit` found no bash suite; actionlint, zizmor, osv-scanner absent; no Roslyn analyzer sweep (C# stack `degraded: no Tier 1 analyzer sweep`, out of focus). Coverage source: `absent` — `coverage=collect` ran `dotnet test` with the XPlat collector, hit the 15-minute cap and was killed (`coverage: absent`, TestQuality carries Tier 0/1 evidence only). Tracked tree after the run differs only by the backlog rows and this report directory. The repo is already heavily audited: most test-quality shapes (timing, inconclusive, untested eng scripts) are live rows and were merged, not refiled.

## Coverage matrix

| Dimension | Tier 0 | Tier 1 | Tier 2 |
|---|---|---|---|
| TestQuality | run (Ignore/Inconclusive/Sleep/pragma/TODO nets; assertion-free scan, demoted) | run: `test-audit: no bash suite`; lens items 1-6 read for timing, premise, tautology samples | skipped: coverage `absent`, no hotspot ranking by coverage-gap |
| Contract | run (policy vs gate grep) | `ci-map` ran, 2 findings refuted | skipped: no C# semantic tools loaded (out-of-scope paths) |
| Security | run (gitleaks + manual workflow read; 11 hits triaged) | `ci-lens: actionlint absent`, `ci-lens: zizmor absent`, `ci-durations` ran | skipped: n/a for workflows/scripts |
| other 11 dimensions | out-of-focus | out-of-focus | out-of-focus |

`ci-map: ran` 2 findings. `ci-durations: ran 30 runs .github/workflows/ci.yml pull_request 40701005805..40803366211`; `ci-durations: ran 30 runs .github/workflows/publish-nuget.yml push` (window ids in the tool JSON). `test-audit: no bash suite`. `ci-lens: actionlint absent`. `ci-lens: zizmor absent`. CI lens shape "missing caching": `validate` and `quality` restore the NuGet cache; "serial steps": none found; "shard imbalance": `ci-durations` shard-imbalance on windows-hosted-1-of-4 (1.58x siblings), out-of-focus, no row.

## Findings

| # | Severity | Dimension | Title | Row |
|---|---|---|---|---|
| 1 | Medium | Security | Pin and checksum-verify the mcp-publisher download in publish-registry (`publish-nuget.yml:199`, `id-token: write` job runs `releases/latest`) | `security-mcp-publisher-unpinned-download` |
| 2 | Medium | TestQuality | Replace wall-clock pass/fail bounds in two correctness tests with structural assertions (`WorkspaceToolsIntegrationTests.cs:468` 1000 ms, `ValidationToolsIntegrationTests.cs:423` 30 s) | `testquality-elapsed-bound-in-correctness-tests` |
| 3 | Low | Security | Extend the run-body interpolation guard to ci.yml and bind its expressions through env (`ci.yml:313` `github.base_ref`; guard at `PublishVersionContextTests.cs:181` covers publish only) | `security-ci-base-ref-interpolated-into-run` |
| 4 | Low | Contract | Gate `Changed — BREAKING` fragments on a migration note and an existing ADR (`verify-changelog-fragments.ps1:27` checks category only) | `contract-breaking-fragment-adr-migration-ungated` |

Counterarguments, evidence and acceptance are in `findings.json` and each row's `items/<id>.md`. No row carries an observed failure of its own: all four are Tier 0 judgment, confidence `medium`.

## Dropped ledger

See [_dropped.md](_dropped.md). 16 entries: 2 refuted ci-map duplicates (jobs are mutually exclusive), 4 merged into live rows, 4 out-of-focus groups, 3 refuted, 3 below-floor.

## Backlog

Status `applied`; no prior `findings.json` under `ai_docs/audits/` matched the `refactor` scope, so fingerprint dedup ran against the live backlog only.

| Op | Id | Pri | Size | Result |
|---|---|---|---|---|
| add | security-mcp-publisher-unpinned-download | Medium | S | exit 0, lint 0 ERROR; anchor overlap with `ci-merge-gate-publish-gate-shape-divergence`, `release-cut-maintenance-branch-support` (accepted) |
| add | security-ci-base-ref-interpolated-into-run | Low | S | exit 0 |
| add | contract-breaking-fragment-adr-migration-ungated | Low | M | exit 0 |
| add | testquality-elapsed-bound-in-correctness-tests | Medium | S | exit 0; overlap with `test-validation-build-shared-fixture`, `workspace-close-isolated-nuget-handles`, `workspace-id-named-args-symbol-navigation` (accepted) |

Resolved candidates: none to list.

## Out-of-focus observations (Directive #3; no rows written, recommend rows)

- `eng/gen-mcp-tool-metadata.ps1`: header says one-time generator to run before deleting the static `Tools` list; `ServerSurfaceCatalog.cs` has no `Tool("…"` rows left, the script is referenced nowhere and untested. DeadCode; recommend a delete-or-justify row.
- `eng/list-skills.ps1`: no test references it by filename. Recommend verifying the `installed_as` contract test named in commit 638fa1f6 still covers it.
- `ci-map.mjs` flags `duplicate-command` for jobs that are mutually exclusive via `if:`. Tooling defect in `~/.claude`, not filed from this audit.
- Re-open the refuted `NUGET_API_KEY` environment finding if a second collaborator is ever added.
