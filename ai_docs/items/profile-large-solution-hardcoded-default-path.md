# profile-large-solution-hardcoded-default-path — Remove the hardcoded maintainer default path

**row:** `profile-large-solution-hardcoded-default-path` · **pri:** `Low` · **size:** `S`

# profile-large-solution-hardcoded-default-path — Remove the hardcoded maintainer default path

## Anchors

- `eng/profile-large-solution.ps1`

## Acceptance

- [ ] -SolutionPath has no maintainer-machine default.
- [ ] docs/large-solution-profiling-baseline.md invocation still valid.

## Evidence

- eng/profile-large-solution.ps1:2; copilot-instructions.md forbids hardcoded environment-specific values. (doc-audit 2026-09-23)
