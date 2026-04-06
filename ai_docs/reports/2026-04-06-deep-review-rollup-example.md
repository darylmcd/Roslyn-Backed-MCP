# Deep-review rollup example

<!-- purpose: Illustrative example of a synthesized deep-review rollup built from multiple raw repo audits. -->

This file is an **example only**. It demonstrates the expected shape of a synthesized rollup after raw audit files have been imported into `ai_docs/audit-reports/`.

## Scope
- **Generated:** 20260406T190000Z
- **Purpose:** Example multi-repo deep-review batch
- **Input audit count:** 3
- **Server / catalog:** Example `server_info` aggregate based on a single release line
- **Clients:** one full-surface client, one constrained client

## Input audits
| Repo | Date | Client | Revision | Server | File |
|------|------|--------|----------|--------|------|
| `sample-solution` | 2026-04-06 | Full-surface client 1.0 | `abc1234` | `roslyn-mcp 1.6.0` | `ai_docs/audit-reports/20260406T170500Z_sample-solution_mcp-server-audit.md` |
| `customer-large-solution` | 2026-04-06 | Full-surface client 1.0 | `def5678` | `roslyn-mcp 1.6.0` | `ai_docs/audit-reports/20260406T172200Z_customer-large-solution_mcp-server-audit.md` |
| `legacy-di-solution` | 2026-04-06 | Constrained client 5.4 | `9876fed` | `roslyn-mcp 1.6.0` | `ai_docs/audit-reports/20260406T175900Z_legacy-di-solution_mcp-server-audit.md` |

## Repo matrix coverage
| Bucket | Covered | Evidence | Notes |
|--------|---------|----------|-------|
| Small or single-project repo | yes | `sample-solution` | Smoke lane covered. |
| Multi-project repo with tests | yes | `sample-solution`, `customer-large-solution` | Core build/test and cross-project behaviors covered. |
| DI-heavy repo | yes | `legacy-di-solution` | Constrained client still exercised DI discovery paths. |
| Source-generator repo | no | — | No suitable repo in this batch. Keep as matrix gap, not a server bug. |
| Central Package Management or multi-targeting repo | no | — | Missing from this batch; schedule for next full-matrix pass. |
| Large solution representative repo | yes | `customer-large-solution` | Use with profiling notes when performance matters. |

## Client coverage
| Client | Full-surface | Notes |
|--------|--------------|-------|
| Full-surface client 1.0 | yes | Resources and prompts invoked directly. |
| Constrained client 5.4 | no | Prompt invocation unavailable; prompt rows remain `blocked` in the raw ledger. |

## Deduped issues
| Key | Severity | Evidence | Backlog action |
|-----|----------|----------|----------------|
| `semantic_search modifier-sensitive query gap` | medium | Seen in `sample-solution` and `customer-large-solution` raw audits | Update existing backlog row `semantic-search-async-modifier-doc` |
| `apply token workspace-version mismatch message vague` | medium | Seen in `customer-large-solution` raw audit | Open one new backlog row with both repros when a second repo confirms |
| `resource prompt surface blocked by constrained client` | low | Seen only in `legacy-di-solution` raw audit | Keep in rollup; client limitation only |

## Blocked-by-client summary
- The constrained client could not invoke prompts directly, so prompt coverage in that lane is informative for client UX but not a server defect by itself.
- Resource invocation remained available only in the full-surface lane; do not generalize constrained-client `blocked` rows into backlog defects without corroborating server-side evidence.

## Candidate closures
| Source id | Evidence | Notes |
|-----------|----------|-------|
| `vuln-scan-network-mock` | `sample-solution` raw audit | No repro in a connected environment; keep open until an air-gapped lane is tested. |
| `semantic-search-async-modifier-doc` | `sample-solution`, `customer-large-solution` raw audits | Still reproduces; not a closure candidate. |

## Backlog actions
- Update `semantic-search-async-modifier-doc` with new repro evidence from two repo shapes.
- Do not open a backlog row for constrained-client prompt blocking; keep it in the rollup unless the client/plugin workflow gets its own tracking lane.
- Schedule one follow-up batch that includes a source-generator repo and a Central Package Management or multi-targeting repo.