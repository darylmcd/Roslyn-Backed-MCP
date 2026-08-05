# validate-recent-git-changes-status-timeout-false-clean — surface git-status timeouts as degraded, not clean

**row:** `validate-recent-git-changes-status-timeout-false-clean` · **pri:** `Medium` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:22,57,81` (`_gitStatusTimeout` / `DefaultGitStatusTimeout` = 10s)
- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:600-619` (`GetGitChangedFilesAsync` — on timeout returns `(Array.Empty<string>(), new[] { "git status exceeded the timeout..." })`, i.e. an EMPTY changed-file list treated downstream as accurate)
- tests under `tests/RoslynMcp.Tests/Services/WorkspaceValidationServiceTests.cs` (or equivalent)

## Acceptance

- [ ] On git-status timeout, `GetGitChangedFilesAsync`'s caller marks the result as degraded/unknown (a distinct status value, not folded into `changedFilePaths: []` as if genuinely clean) — the warning string already carries `retryable=true`, but nothing downstream currently branches on it
- [ ] `overallStatus`/`changedFilePaths` never silently present a git-status timeout as "no changes"
- [ ] The 10s `DefaultGitStatusTimeout` is configurable (or a documented larger default) so large/dirty repos don't routinely hit this fallback
- [ ] Regression test: simulate a `git status` timeout, assert the result carries a distinct degraded signal rather than an empty-but-trusted changed-file list

## Evidence

- `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2a `validate_recent_git_changes` row + §3 pattern 7. 1 codex session (`019faa80` c605), but high materiality: `overallStatus: clean` and `changedFilePaths: []` reported despite ~30 real uncommitted files on disk; recurred identically a second time (`call_3jrHYa6U9KoKdXuurgKC29aD`, ~40 min later) in the same session. Agent only caught the real state via a manual `git status --short` check near session end.

## Context

Source verification (2026-08-05, this row's authoring pass) distinguished this from an already-shipped, similarly-worded fix: `validate-recent-git-changes-timeout` (closed gh #759) addressed the BROADER `InternalValidationTimeoutException`/`RunValidationPhaseAsync` timeout class (governed by `_validationPhaseTimeout`), which DOES return a structured retryable-timeout result with `changedFilePaths reflects the scope that was being validated` (`WorkspaceValidationService.cs:818-831`). This row's bug is a NARROWER, DIFFERENT timeout: the `git status` subprocess call's own dedicated 10-second `_gitStatusTimeout` (`GetGitChangedFilesAsync`). On THIS timeout, the current code returns an empty array + a warning string, and the caller falls back to validating the "full workspace" instead of the git-changed scope — but nothing distinguishes "genuinely zero git changes" from "we don't actually know, git status didn't answer in time." The `retryable=true` signal already exists in the warning TEXT but is not wired into the structured status/changedFilePaths the caller actually branches on.

## Notes

Do not conflate this with the closed `validate-recent-git-changes-timeout` (gh #759) — that fix is confirmed live and correct for its class of timeout; this is a sibling gap in a different code path within the same file.
