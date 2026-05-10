# move-to-git-issues end-to-end verification — fresh-session prompt

<!-- purpose: One-shot verification prompt for the merged `move-to-git-issues` design (Rows 1-3, PRs #591/#592/#593). Paste into a fresh Claude Code session inside `C:/Code-Repo/Roslyn-Backed-MCP`; the new skill must be loaded first. -->

---

## Prompt to paste

> You are verifying the end-to-end funnel from the `move-to-git-issues` design, which shipped across three PRs that merged earlier today: [#591](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/591) (Row 1, ship `/mcp-server-surface-test`), [#592](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/592) (Row 2, shared renderer + `/backlog-intake --publish`), [#593](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/593) (Row 3, issue template + label seeder + lockstep tests). The renderer-level refusal contract (P0 / `area: security`), the `gh issue create` command shape, the dry-run label seed, and GitHub's recognition of the issue template were all verified in the previous session. **What you need to verify here is the half that requires the new skill to actually run** — `/mcp-server-surface-test --quick` and `/backlog-intake --publish`.
>
> Work in auto mode. Squash-merge / share state only when the steps below explicitly authorize it.
>
> ### Preflight
>
> 1. Confirm the new skill is loaded. Run `/plugin update roslyn-mcp@roslyn-mcp-marketplace` if `/mcp-server-surface-test` is not in your skill surface; if `/plugin update` is unavailable in this client, restart and try again. The skill must appear in the user-invocable list before you proceed.
> 2. Confirm the Roslyn MCP server is reachable: `mcp__roslyn__server_info` must return `connection.state: "ready"`. If not, start the server (`dotnet tool run roslynmcp` or whatever the host's entry is) and re-check.
> 3. Confirm `gh` is on PATH and `gh auth status` reports authenticated against `darylmcd/Roslyn-Backed-MCP`.
>
> ### Step 0 — Seed the issue labels (one-shot, idempotent)
>
> Run `pwsh -NoProfile -File scripts/seed-issue-labels.ps1`. This creates the `area:*` and `severity:*` labels at `darylmcd/Roslyn-Backed-MCP`. The script is idempotent (uses `gh label create --force`); re-running is safe. After the script finishes, confirm via `gh label list --repo darylmcd/Roslyn-Backed-MCP --search "area:"` that 7 area labels and 3 severity labels exist.
>
> ### Step 1 — `--quick` against a non-Roslyn target (stdout-only)
>
> Pick `C:/Code-Repo/DotNet-Firewall-Analyzer` as the target. Invoke `/mcp-server-surface-test --quick C:/Code-Repo/DotNet-Firewall-Analyzer`. Expect:
> - Runtime ≤15 min.
> - Audit report lands at `C:/Code-Repo/DotNet-Firewall-Analyzer/audit-reports/<ts>_<repo-id>_mcp-server-surface-test.md`.
> - **No `backlog.d/` writes anywhere** (`--quick` is read-only and consumer-side; no fragment emission).
> - **No `gh` invocations** (no `--auto-file`).
> - Phase 19 either prints actionable findings to stdout in the envelope shape, or records `**N/A — no actionable findings**`.
>
> Verification commands:
> - `git -C C:/Code-Repo/DotNet-Firewall-Analyzer status --short` — should show only the new `audit-reports/` file (untracked).
> - `pwsh -NoProfile -File C:/Code-Repo/Roslyn-Backed-MCP/eng/verify-skills-are-generic.ps1` should still pass after the audit.
>
> If Phase 19 produces zero actionable findings (likely on a small target), capture that in the report; it does not invalidate the verification.
>
> ### Step 2 — `--quick --auto-file` against the same target (filing path)
>
> Re-invoke `/mcp-server-surface-test --quick --auto-file C:/Code-Repo/DotNet-Firewall-Analyzer`. The skill should:
> - Still produce the audit report (Step 1's contract).
> - For each non-refused finding, call `gh issue create --repo darylmcd/Roslyn-Backed-MCP --title <id> --label area:<area> --label severity:<severity> --body-file <tempfile>`.
> - Print the returned Issue URLs after Phase 19 completes.
>
> If Phase 19 produces zero actionable findings, **synthesize one** by hand: dot-source `skills/mcp-server-surface-test/lib/render-finding.ps1` and craft a `[hashtable]$f` with `severity='P3'`, `area='docs'`, `id='verify-end-to-end-quick-auto-file-probe'`, plausible anchors, then call `Render-FindingIssue` and `gh issue create` with the result. This proves the path even when the audited repo is too clean to surface real findings.
>
> Verification:
> - The filed Issue must use the new template — title is the finding id, body is the rendered envelope (no double labels, no garbled em-dashes).
> - Both `area:<value>` and `severity:<value>` labels must apply (Step 0 seeded them).
> - **Close the test Issue** after verification with `gh issue close <number> --reason "not planned" --comment "end-to-end verification probe; see https://github.com/darylmcd/Roslyn-Backed-MCP/pull/593"`.
>
> ### Step 3 — `--full` against `Roslyn-Backed-MCP` itself (canonical smoke)
>
> Skip this step unless you have 90–180 min to spare and the self-hosted runner is idle (the runner and a local `--full` invocation contend for the same MSBuild server / VBCSCompiler / file locks; per the `feedback_self_hosted_runner_no_duplicate_local_run` memory in `~/.claude/projects/C--Code-Repo-Roslyn-Backed-MCP/memory/`, never run heavy local validation while the runner is active).
>
> When you do run it: `/mcp-server-surface-test --full` from inside `C:/Code-Repo/Roslyn-Backed-MCP`. Expect:
> - Disposable worktree at `../Roslyn-Backed-MCP-surface-test-<ts>` (or similar).
> - Audit report at `audit-reports/<ts>_roslyn-backed-mcp_mcp-server-surface-test.md`.
> - Promotion-scorecard JSON at `audit-reports/_latest-promotion-scorecard.json`.
> - **Worktree torn down at run end** (`git worktree list` should not show the disposable worktree after `--full` returns).
> - The audit report must contain zero banned patterns when piped through `pwsh -Command "(Get-Content <report>) | Select-String -Pattern '(state\\.json|backlog-sweep|schemaVersion|ai_docs/|backlog\\.md|eng/|just verify-|Directory\\.Build\\.props|BannedSymbols\\.txt)'"` — paths in the audited repo's content (e.g. `ai_docs/` mentions of this repo's own docs) are fine; what matters is that the prompt didn't drift back toward repo-coupled wording.
>
> ### Step 4 — `/backlog-intake --publish` (maintainer publish path)
>
> This step is chicken-and-egg without real fragments to consume. Two options:
>
> 1. **Real path** — wait until the next maintainer audit produces actual `backlog.d/<id>.md` fragments under audited sibling repos; then run `/backlog-intake --publish`. Confirm: each accepted row gets an Issue link appended in `ai_docs/backlog.md`; refused rows (P0 / `area: security`) print the SECURITY banner and do not file.
> 2. **Synthetic path** — create a minimal fragment under `<some-repo>/backlog.d/verify-end-to-end-publish-probe.md` with valid frontmatter (use the schema doc + the renderer's `Render-FindingFragment` to generate it), run `/backlog-intake`, and confirm the file is consumed and a row appended. Then re-run with `--publish` to confirm the gh-invocation path. Close the synthetic Issue afterward.
>
> ### Step 5 — Already verified in the previous session, but re-confirm
>
> Synthesize a P0 finding and a `area: security` finding. Run `Render-FindingIssue` on each. Both must report `refusedPublic=True` and prepend the SECURITY banner to the body. Both auto-file paths (`--auto-file` in `/mcp-server-surface-test`, `--publish` in `/backlog-intake`) must short-circuit before any `gh issue create` invocation. **Do not** invoke `gh issue create` for either probe.
>
> ### Reporting
>
> After all steps complete, post a comment on [PR #593](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/593) (or open a fresh `chore: e2e verification` PR if changes were committed) listing:
> - Which steps ran end-to-end vs which were synthesized.
> - Issue URLs created and closed.
> - Any drift between the renderer's emitted body and what GitHub displayed in the rendered Issue.
> - Wall-clock for `--quick` and (if run) `--full`.
> - Whether the disposable-worktree teardown was clean (Step 3 only).
>
> Do not push, comment, or file Issues outside what these steps explicitly authorize. If you hit a real bug during the verification, file it via the new template (`/mcp-server-surface-test --auto-file` will do this) — that itself is end-to-end evidence.

---

## Notes

- **The full plan** that drove Rows 1–3 is at `C:/Users/daryl/.claude/plans/ready-to-start-looking-harmonic-toast.md` (the "End-to-end verification" section is what this prompt operationalizes).
- **Skill auto-load on Claude Code Windows** historically requires the rename-step workaround per the `feedback_plugin_install_eperm` memory at `~/.claude/projects/C--Windows-system32/memory/`. If `/plugin update` errors, run `claude plugin install roslyn-mcp@roslyn-mcp-marketplace` from CLI and reapply the manual rename.
- **Self-hosted runner contention** — never run `--full` (or full `dotnet test`) locally while the runner is active. See `~/.claude/projects/C--Code-Repo-Roslyn-Backed-MCP/memory/feedback_self_hosted_runner_no_duplicate_local_run.md`.
