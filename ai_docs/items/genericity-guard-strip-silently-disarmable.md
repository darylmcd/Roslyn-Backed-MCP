# genericity-guard-strip-silently-disarmable — three reproduced ways to slip a banned marker past the gate

## Anchors

- `eng/verify-skills-are-generic.ps1`
- `tests/RoslynMcp.Tests/Skills/McpServerSurfaceTestSkillTests.cs`

## Acceptance

- [ ] Placeholder strip bounded to a path charset and an allowlisted placeholder set (`<audited-repo-root>`, `<Roslyn-Backed-MCP-root>`) instead of `<[^>]+-root>/\S+`, so an adjacent banned marker in the same whitespace-delimited token still trips.
- [ ] `Get-Content` result forced to an array so single-line `.md` files are scanned line-wise rather than character-wise.
- [ ] Probe fixtures cover all four reproduced shapes: table-cell, no-space-comma, laundered `<repo-root>`, and single-line file.

## Evidence

All shapes REPRODUCED during the PR #1240 review by running the shipped script against scratch fixtures — not reasoned about:

1. **Greedy strip.** A markdown table row `|\`<audited-repo-root>/backlog.d/\`|\`eng/stage-review-inbox.ps1\`|` exits 0, while the space-separated equivalent correctly exits 1. The banned marker is erased because it falls inside the same whitespace-delimited run as the placeholder path. These prompt files are table-heavy.
2. **Name-agnostic placeholder.** `<repo-root>/ai_docs/domains/tool-usage-guide.md` exits 0 — any author can neutralize the gate on a genuinely repo-coupled path by inventing a `<x-root>/` prefix.
3. **Scalar Get-Content.** Identical banned content passes as a 1-line file and fails as line 3 of a 3-line file, because `Get-Content` returns a scalar String for a single-line file and the index then walks characters. Latent today (no <=1-line file under `skills/`), but newly reachable because PR #1240 widened the glob from `SKILL.md` to `*.md`.

Across all 38 real shipped `.md` files the strip currently hides only intended placeholder-rooted spans, so this is a latent weakening rather than a live leak.

## Context

PR #1240 widened the guard from `skills/**/SKILL.md` to `skills/**/*.md` and added the placeholder strip. That is a large net improvement and shipped on its own merits; these are hardening follow-ups against the newly-added strip, filed rather than fixed inline because they were medium-severity advisory findings.
