# dependabot-groups-batching — batch minor/patch Dependabot bumps into one PR per ecosystem

**row:** `dependabot-groups-batching` · **pri:** `Low` · **size:** `S`

## Anchors

- `.github/dependabot.yml`

## Acceptance

- [ ] Each ecosystem (`github-actions`, `nuget`) has a `groups:` entry batching `update-types: [minor, patch]` into a single weekly PR.
- [ ] Major bumps remain individual PRs so breaking changes get isolated review.

## Evidence

- Code-quality review of `repo-dependabot-config` (2026-06-19 top-n-remediation): the initial `dependabot.yml` ships without grouping, so every routine minor/patch action+nuget bump opens its own PR (noise), capped only by `open-pull-requests-limit: 5`.

## Context

Nice-to-have follow-on from the Dependabot config row. Non-blocking; reduces PR churn once Dependabot starts opening update PRs.
