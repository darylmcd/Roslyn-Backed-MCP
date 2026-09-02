# release-header-regex-crlf-intolerant — CRLF-safe release-header matching

**row:** `release-header-regex-crlf-intolerant` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-breaking-version-bump.ps1:151`

## Acceptance

- [ ] The release-header pattern matches `## [X.Y.Z] - YYYY-MM-DD` in a CRLF working-tree copy as well as an LF one (`[ \t]*\r?$`, or match against normalized text).
- [ ] A regression test asserts the gate passes on a CRLF-line-ending CHANGELOG fixture and still rejects a genuinely missing release section.
- [ ] The zero-match failure message distinguishes "no release section" from "release section present but unparseable", so the operator is not sent looking for a missing heading that is plainly there.
- [ ] Sweep sibling release gates for the same `[ \t]*$` shape and fix any that share it.

## Evidence

- Hit during the 2026-09-02 v4.1.2 release cut. `verify-release.ps1` aborted at `verify-breaking-version-bump.ps1:154` with `CHANGELOG.md has no released ## [X.Y.Z] section.` while `## [4.1.2]` and eleven prior headers were present.
- Measured on the same file: LF copy -> 79 strict matches; CRLF copy -> 0. Confirmed identically in .NET (`[regex]::Matches`) and Python, so it is the pattern, not the host.
- `git diff` hides the trigger: git normalizes CRLF for diff and on commit, so the working tree can be CRLF while the diff looks clean and the commit would be correct. Only the worktree-reading gate fails.

## Context

`$releaseHeaderPattern` ends `[ \t]*$`. In both engines `$` under `(?m)` anchors before `\n`, not before the `\r` of a `\r\n`, so every header fails on a CRLF checkout. This repo stores LF and checks out LF, so the gate passes today; any clone with `core.autocrlf=true` would hit a fail-closed release gate with a misleading message. Fail-closed is right; the message and the CRLF blindness are not. One-character fix plus a fixture.
