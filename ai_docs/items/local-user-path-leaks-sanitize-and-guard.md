# local-user-path-leaks-sanitize-and-guard — Sanitize local user-profile paths from tracked files and gate new ones

**row:** `local-user-path-leaks-sanitize-and-guard` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-ai-docs.ps1`
- `docs/self-hosted-runner.md`

## Acceptance

- [ ] No tracked file contains a real local user-profile path (drive-letter Users/<name>, a Claude project slug derived from it, or the per-user Claude temp scratch root). Placeholders such as `<user>` and documented examples such as `C:\Users\foo` remain.
- [ ] docs/self-hosted-runner.md PowerShell derives the runner root from `$env:USERPROFILE` instead of a literal profile path, and its behavior is unchanged on the original host.
- [ ] verify-ai-docs.ps1 fails on any tracked or untracked-not-ignored text file that introduces a non-placeholder user-profile path, and self-tests representative accept/reject samples.
- [ ] The guard runs on every CI route, including evidence-lint.

## Evidence

- 2026-09-24 leak scan of main after PR #1607: 211 occurrences of the maintainer's Windows profile path across 9 tracked files (2026-08-25 logging-audit stderr, two multisession retros, two backlog-remediate state.json files, a plan stanza, an items file, docs/self-hosted-runner.md). Only placeholder `foo` otherwise.

## Context

- The repo is public. Git history keeps the old content; this row stops new leaks and cleans the current tree. A purge of history would need a separate, explicit decision.
- The sanitizers in the audit prompts caught some cases but not all: URL-encoded paths and project slugs slipped through during the 2026-09-24 audit. A repository gate is the durable fix.
