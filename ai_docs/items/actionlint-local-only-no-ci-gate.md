# actionlint-local-only-no-ci-gate — `just ci` runs actionlint but no CI workflow does

**row:** `actionlint-local-only-no-ci-gate` · **pri:** `Medium` · **size:** `M`

## Anchors

- `justfile:83`
- `eng/verify-actionlint.ps1`
- `.github/workflows/ci.yml`

## Acceptance

- [ ] Decide whether actionlint is a merge gate. If yes, `ci.yml` runs `eng/verify-actionlint.ps1` on the artifact-owner leg (including docs-only PRs that touch `.github/workflows/**`). If no, `just ci` stops including it, or its comment stops calling itself the local equivalent of the required PR pipeline.
- [ ] `ai_docs/prompts/backlog-sweep-addenda.md`'s `just ci` note is updated to match the decision.

## Evidence

`justfile:83` declares `ci: verify-docs verify-skills verify-changed-format verify-actionlint verify-release-pr vuln-audit` under the comment "Local equivalent of the required pull-request pipeline". `grep -rn actionlint .github/workflows/` returns nothing: neither `ci.yml` nor `publish-nuget.yml` runs it. A workflow-syntax regression can therefore merge green while `just ci` fails locally, or a contributor who skips `just ci` never sees it.
