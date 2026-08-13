# genericity-guard-ps1-csharp-duplication — gate and echo duplicate patterns across two languages

## Anchors

- `eng/verify-skills-are-generic.ps1`
- `tests/RoslynMcp.Tests/Skills/McpServerSurfaceTestSkillTests.cs`

## Acceptance

- [ ] Banned patterns and both pre-scan strip regexes live in ONE data file (e.g. `eng/banned-skill-markers.json`) read by both the PowerShell gate and the C# test; neither re-declares them.
- [ ] The test pins the gate's file glob (asserts the PS1 scans `skills/**/*.md`) so a lone revert of the glob line fails `dotnet test`.

## Evidence

Verified during the PR #1240 review: the banned-pattern list and both strip regexes are byte-duplicated between `eng/verify-skills-are-generic.ps1` and `McpServerSurfaceTestSkillTests.cs`, enforced only by a `MUST stay byte-parallel` source comment. The C# echo enumerates `skills/**/*.md` independently rather than reading the PS1's glob, so reverting the PS1's glob line alone leaves `dotnet test` green — the spec reviewer independently flagged the same drift channel.

Acceptable today because the PS1 is the canonical gate in CI (invoked from the justfile, `verify-release.ps1`, and `verify-ai-docs.ps1`) and the two copies cover each other's regressions — but that is a comment-enforced invariant, which is what this row removes.

## Context

Land AFTER `genericity-guard-strip-silently-disarmable` — both edit `eng/verify-skills-are-generic.ps1` and would collide as concurrent PRs.
