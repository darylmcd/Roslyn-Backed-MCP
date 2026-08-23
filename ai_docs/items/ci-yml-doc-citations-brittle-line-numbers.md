# ci-yml-doc-citations-brittle-line-numbers — replace brittle ci.yml:<line> citations with anchor names

**row:** `ci-yml-doc-citations-brittle-line-numbers` · **pri:** `Low` · **size:** `S`

## Anchors

- `docs/self-hosted-runner.md:19,170,203`
- `eng/verify-ai-docs.ps1` (only if the optional regression guard is implemented)

## Acceptance

- [ ] No `ci.yml:<line>` / `ci.yml:<start>-<end>` citation remains in `docs/self-hosted-runner.md`; each is replaced by a job/step name (`route` job, `Decide target runner` step, `validate`'s `runs-on:`) that survives future line shifts.
- [ ] Optionally extend `eng/verify-ai-docs.ps1` to flag `\.ya?ml:\d+` citations in tracked markdown so this class cannot regress silently.

## Evidence

- Found during code-quality review of PR #1211 (`ci-runner-offline-hosted-fallback-router`): a same-PR follow-up fix commit shifted `ci.yml` line numbers by 7 lines, silently invalidating three citations the first commit had written correctly. `verify-ai-docs.ps1` checks only a fixed stale-pattern list plus relative-link targets, so nothing caught it.

## Context

Spin-off from the code-quality review of PR #1211. Not fixed inline because it's a doc-hygiene/tooling hardening item, not required by that row's acceptance criteria.
