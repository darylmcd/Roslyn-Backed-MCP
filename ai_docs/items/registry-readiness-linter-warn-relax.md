# registry-readiness-linter-warn-relax — relax the repository-url-matches-name warn

**row:** `registry-readiness-linter-warn-relax` · **pri:** `Low` · **size:** `S`

## Anchors

- `eng/verify-registry-readiness.ps1:142-149` (`repository-url-matches-name` check)

## Acceptance

- [ ] The check no longer warns when the server name's second segment differs from the repo name, as long as the name is `io.github.<owner>/*` and `<owner>` matches the `repository.url` owner. GitHub auth grants the whole `io.github.<owner>/*` namespace, so `io.github.darylmcd/roslyn-mcp` against repo `Roslyn-Backed-MCP` is valid, not suspect.
- [ ] A genuinely wrong owner (name owner ≠ repo owner) still warns/fails.

## Evidence

- 2026-06-19 registry-publish: `verify-registry-readiness.ps1` reported `ready (26 pass / 0 fail / 1 warn)`; the lone warn was this false positive. Publish then succeeded — `mcp-publisher` confirmed `✓ Server io.github.darylmcd/roslyn-mcp version 2.3.5`.

## Context

Cosmetic only (warn, non-blocking) but it now fires on every run, training maintainers to ignore the linter's warnings. Tighten to owner-namespace matching.
