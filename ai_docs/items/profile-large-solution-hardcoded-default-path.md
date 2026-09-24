# profile-large-solution-hardcoded-default-path — Remove the hardcoded maintainer default path

**row:** `profile-large-solution-hardcoded-default-path` · **pri:** `Low` · **size:** `S`

# profile-large-solution-hardcoded-default-path — Remove the hardcoded maintainer default path

## Anchors

- `eng/profile-large-solution.ps1`
- `docs/large-solution-profiling-baseline.md`
- `ai_docs/prompts/profile-large-solution.md`

## Acceptance

- [ ] eng/profile-large-solution.ps1 -SolutionPath is mandatory, with no maintainer-machine default.
- [ ] docs/large-solution-profiling-baseline.md no longer describes C:\Code-Repo\OrchardCore as the default target. It names the requirement (any 50+ project solution) and how to reproduce the baseline (clone OrchardCore at f852c64).
- [ ] ai_docs/prompts/profile-large-solution.md treats the target solution as an operator-supplied input rather than a pre-provisioned local checkout.
- [ ] The 2026-04-26 recorded run stays as historical evidence (it backs the workspace-process-pool-or-daemon Defer gate).

## Evidence

- eng/profile-large-solution.ps1:2 defaults -SolutionPath to C:/Code-Repo/OrchardCore/OrchardCore.slnx; copilot-instructions.md forbids hardcoded environment-specific values. (doc-audit 2026-09-23)
- Operator 2026-09-24: the local OrchardCore checkout was only for initial large-repo testing. It is no longer required or present under C:/Code-Repo, so the default path, docs/large-solution-profiling-baseline.md:37 ("default target"), and the runbook's local-checkout assumption are all stale.

