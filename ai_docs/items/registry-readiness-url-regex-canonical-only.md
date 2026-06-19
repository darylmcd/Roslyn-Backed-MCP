# registry-readiness-url-regex-canonical-only — owner regex accepts only canonical github.com URLs

**row:** `registry-readiness-url-regex-canonical-only` · **pri:** `Low` · **size:** `S`

## Anchors

- `eng/verify-registry-readiness.ps1` — the `repository-url-matches-name` URL-owner regex `^https://github\.com/([a-zA-Z0-9-]+)/`

## Acceptance

- [ ] Decide whether non-canonical github.com URL forms (`http://`, `www.`, `git@github.com:` SCP-style) are worth parsing. If the registry only accepts canonical `https://github.com/` URLs (it does — `mcp-publisher` requires them), close as won't-fix and add a one-line comment stating the canonical-URL-only assumption so the narrow regex reads as intentional.

## Evidence

- Code-quality review of `registry-readiness-linter-warn-relax` (2026-06-19 top-n-remediation): the URL-owner regex rejects non-canonical forms with a "not a parseable github.com URL" warn. Harmless today since the MCP registry mandates canonical `https://github.com/` URLs — flagged Defer/won't-fix-leaning by the reviewer.

## Context

Pure linter-robustness nit on a maintainer release-prep script. Most likely resolution is a one-line clarifying comment rather than broadening the regex.
