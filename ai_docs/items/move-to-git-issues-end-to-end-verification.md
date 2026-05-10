# move-to-git-issues end-to-end verification — fresh-session prompt

<!-- purpose: One-shot verification prompt for the merged `move-to-git-issues` design (Rows 1-3, PRs #591/#592/#593, released as v1.35.1 in PR #595). Paste into a fresh Claude Code session inside `C:/Code-Repo/Roslyn-Backed-MCP`; the new skill must be loaded first. The prompt is self-completing — when every step ends green, the design is officially done. -->

---

## Prompt to paste

> You are running the end-to-end verification of the `move-to-git-issues` design, which shipped across four PRs that merged earlier today: [#591](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/591) (Row 1, ship `/mcp-server-surface-test`), [#592](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/592) (Row 2, shared renderer + `/backlog-intake --publish`), [#593](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/593) (Row 3, issue template + label seeder + lockstep tests), and [#595](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/595) (release v1.35.1). The renderer-level refusal contract, the `gh issue create` command shape, the dry-run label seed, and GitHub's recognition of the issue template were verified in the previous session. **What you need to verify here is the half that requires the new skill to actually run** plus every side-effect the design depends on (real label seed, real Issue, real publish smoke).
>
> Run in auto mode. Every authorized side-effect below is explicitly listed; do nothing visible-to-others outside that list. When all steps end green, you'll commit a one-line update to `ai_docs/items/move-to-git-issues.md` flipping the design status to "completed" and ship that as a chore PR. **That status flip is the load-bearing definition of done — do not stop short of it.**
>
> ### Preflight
>
> 1. Confirm the new skill is loaded. `/mcp-server-surface-test` must appear in your skill surface; if it does not, run `/plugin update roslyn-mcp@roslyn-mcp-marketplace` (or restart and try again — see the `feedback_plugin_install_eperm` memory at `~/.claude/projects/C--Windows-system32/memory/` for the Windows EPERM workaround). Do not proceed past this gate.
> 2. Confirm the Roslyn MCP server is reachable: `mcp__roslyn__server_info` must return `connection.state` of `idle` or `ready`. Capture `version` for later — Step 1 stamps it on synthesized findings.
> 3. Confirm `gh` on PATH and `gh auth status` reports authenticated against `darylmcd/Roslyn-Backed-MCP`.
> 4. Confirm `git status --short` is clean; if not, stash unrelated work first.
>
> ### Step 0 — Seed the issue labels (one-shot, real, authorized)
>
> Run `pwsh -NoProfile -File scripts/seed-issue-labels.ps1` (no `-DryRun`). The script is idempotent (`gh label create --force`); re-running is safe. Confirm post-state:
>
> ```bash
> gh label list --repo darylmcd/Roslyn-Backed-MCP --search "area:" | wc -l   # expect 7
> gh label list --repo darylmcd/Roslyn-Backed-MCP --search "severity:" | wc -l # expect 3
> ```
>
> If counts disagree, stop and diagnose — the lockstep test exists to prevent enum drift, but a test pass doesn't guarantee successful seed propagation.
>
> ### Step 1 — `--quick` against a non-Roslyn target (stdout-only)
>
> Pick `C:/Code-Repo/DotNet-Firewall-Analyzer` as the target. Invoke `/mcp-server-surface-test --quick C:/Code-Repo/DotNet-Firewall-Analyzer`. Expect:
> - Runtime ≤15 min.
> - Audit report at `C:/Code-Repo/DotNet-Firewall-Analyzer/audit-reports/<ts>_<repo-id>_mcp-server-surface-test.md`.
> - **No `backlog.d/` writes anywhere** (`--quick` is read-only and consumer-side; no fragment emission).
> - **No `gh` invocations** (no `--auto-file`).
> - Phase 19 either prints actionable findings to stdout in the envelope shape, or records `**N/A — no actionable findings**`.
>
> Verification:
>
> ```bash
> git -C C:/Code-Repo/DotNet-Firewall-Analyzer status --short  # only audit-reports/ untracked
> ```
>
> ### Step 2 — `--quick --auto-file` (file one real public Issue)
>
> Re-invoke `/mcp-server-surface-test --quick --auto-file C:/Code-Repo/DotNet-Firewall-Analyzer`. The skill should call `gh issue create --repo darylmcd/Roslyn-Backed-MCP --title <id> --label area:<area> --label severity:<severity> --body-file <tempfile>` for each non-refused finding.
>
> If Phase 19 produced zero actionable findings (likely on a small target), **synthesize one finding** directly with the renderer and file it manually so the public-template path is exercised end-to-end:
>
> ```pwsh
> . skills/mcp-server-surface-test/lib/render-finding.ps1
> $f = @{
>     id = 'verify-end-to-end-quick-auto-file-probe'
>     source_repo = 'dotnet-firewall-analyzer'
>     severity = 'P3'; area = 'docs'
>     server_version = '<version-from-Preflight-step-2>'
>     anchors = @('README.md:1')
>     finding = 'End-to-end verification probe; safe to ignore. See https://github.com/darylmcd/Roslyn-Backed-MCP/pull/593.'
>     repro = 'No repro required; this is a verification probe.'
>     proposed_fix = 'No fix required.'
> }
> $issue = Render-FindingIssue -Finding $f
> $bf = New-TemporaryFile; $issue.body | Out-File -FilePath $bf.FullName -Encoding utf8 -NoNewline
> gh issue create --repo darylmcd/Roslyn-Backed-MCP --title $issue.title `
>     --label "area:docs" --label "severity:P3" --body-file $bf.FullName
> Remove-Item $bf
> ```
>
> Capture the returned Issue URL (call it `$consumerIssueUrl`).
>
> Verify in the GitHub UI that the Issue:
> - Renders the body fields cleanly (id, source-repo, severity, area, server-version, anchors, finding, repro, proposed-fix).
> - Has both `area:docs` and `severity:P3` labels applied.
> - Title equals the finding `id`.
>
> ### Step 3 — `--full` against `Roslyn-Backed-MCP` itself (canonical smoke; OPTIONAL)
>
> **Skip this step unless the self-hosted runner is idle and you have 90–180 min available.** Per `~/.claude/projects/C--Code-Repo-Roslyn-Backed-MCP/memory/feedback_self_hosted_runner_no_duplicate_local_run.md`, never run heavy local validation while the runner is active.
>
> If you do run it: `/mcp-server-surface-test --full` from inside `C:/Code-Repo/Roslyn-Backed-MCP`. Expect a disposable worktree at `../Roslyn-Backed-MCP-surface-test-<ts>` torn down at run end, an audit report at `audit-reports/<ts>_roslyn-backed-mcp_mcp-server-surface-test.md`, a promotion-scorecard JSON sibling, and zero `git worktree list` entries for the disposable worktree after return. Note any drift in the report's prose (paths/markers that should not be there) and surface it as a finding.
>
> ### Step 4 — `/backlog-intake --publish` (synthetic fragment path)
>
> Real fragments only show up after a maintainer audit. Synthesize one to exercise the publish path end-to-end:
>
> ```pwsh
> $repo = 'C:/Code-Repo/DotNet-Firewall-Analyzer'
> mkdir -Force "$repo/backlog.d" | Out-Null
> . skills/mcp-server-surface-test/lib/render-finding.ps1
> $f = @{
>     id = 'verify-end-to-end-publish-probe'
>     source_audit = 'verify-end-to-end-publish-probe-source.md'
>     source_repo = (Get-FindingRepoId -RepoRoot $repo)
>     severity = 'P3'; area = 'docs'
>     server_version = '<version-from-Preflight-step-2>'
>     anchors = @('README.md:1')
>     finding = 'End-to-end verification probe; safe to ignore.'
>     repro = 'No repro required.'
>     proposed_fix = 'No fix required.'
> }
> $fragmentPath = "$repo/backlog.d/verify-end-to-end-publish-probe.md"
> Render-FindingFragment -Finding $f | Out-File -FilePath $fragmentPath -Encoding utf8 -NoNewline
> ```
>
> Then invoke `/backlog-intake --publish` from inside `C:/Code-Repo/Roslyn-Backed-MCP`. Expect:
> - The intake skill discovers the synthetic fragment under `<sibling-repo>/backlog.d/`.
> - It appends a row to `ai_docs/backlog.md` for the synthesized finding.
> - It calls `gh issue create` against `darylmcd/Roslyn-Backed-MCP` (Phase 6.5) and appends the returned Issue URL to the row's body.
> - The fragment file is deleted from `<sibling-repo>/backlog.d/` on consumption.
>
> Capture the returned Issue URL (call it `$maintainerIssueUrl`).
>
> ### Step 5 — Refusal contract (re-confirm; no Issue should be filed)
>
> Run two refusal probes through the renderer with `Test-FindingShouldRefusePublicFile` and `Render-FindingIssue`. **Do not invoke `gh issue create` for either probe.**
>
> ```pwsh
> . skills/mcp-server-surface-test/lib/render-finding.ps1
> $p0 = @{ id='verify-p0'; source_repo='r'; severity='P0'; area='tools'; server_version='1.0'; anchors=@('a:1'); finding='f'; repro='r'; proposed_fix='p' }
> $sec = @{ id='verify-sec'; source_repo='r'; severity='P3'; area='security'; server_version='1.0'; anchors=@('a:1'); finding='f'; repro='r'; proposed_fix='p' }
> Test-FindingShouldRefusePublicFile -Finding $p0    # must be True
> Test-FindingShouldRefusePublicFile -Finding $sec   # must be True
> (Render-FindingIssue -Finding $p0).refusedPublic   # must be True
> (Render-FindingIssue -Finding $sec).refusedPublic  # must be True
> (Render-FindingIssue -Finding $p0).body | Select-String -SimpleMatch 'DO NOT FILE PUBLICLY'   # must match
> (Render-FindingIssue -Finding $sec).body | Select-String -SimpleMatch 'DO NOT FILE PUBLICLY'  # must match
> ```
>
> If any of those four predicates is `False` or either banner check is empty, stop — the renderer's refusal contract regressed and Row 2's load-bearing safeguard is broken.
>
> ### Step 6 — Cleanup (mandatory before close-out)
>
> Close the synthetic Issues you filed in Steps 2 and 4 so the public Issues list isn't polluted with verification cruft:
>
> ```bash
> gh issue close <consumer-issue-number> --reason "not planned" \
>     --comment "End-to-end verification probe per ai_docs/items/move-to-git-issues-end-to-end-verification.md. Closing — see PRs #591/#592/#593/#595 for the underlying design."
> gh issue close <maintainer-issue-number> --reason "not planned" \
>     --comment "End-to-end verification probe per ai_docs/items/move-to-git-issues-end-to-end-verification.md. Closing — see PRs #591/#592/#593/#595 for the underlying design."
> ```
>
> Also revert the synthetic backlog row from Step 4 — open `ai_docs/backlog.md`, find the `verify-end-to-end-publish-probe` row, delete it. Confirm `<sibling-repo>/backlog.d/` is back to its pre-Step-4 state (the fragment was already consumed/deleted by intake).
>
> ### Step 7 — Definition of done (CLOSE THE DESIGN)
>
> Edit `ai_docs/items/move-to-git-issues.md`:
>
> - In the frontmatter HTML comment block at the top, change the `status` line from `design seed - current decisions captured; not yet a backlog row` to `completed - rows 1-3 shipped (PRs #591, #592, #593) and released as v1.35.1 (PR #595); end-to-end verification passed YYYY-MM-DD; row 4 deferred per design note`. Use today's date in YYYY-MM-DD form.
> - Add a new section at the bottom titled `## Verification` recording: which steps ran end-to-end vs which were synthesized, the closed-Issue URLs (from Step 6) for the audit trail, the wall-clock for `--quick` (and `--full` if Step 3 ran), and the date.
>
> Then ship the doc edit + the `ai_docs/backlog.md` synthetic-row revert as a single chore PR. Use the `/ship` skill with default squash merge:
>
> ```
> /ship docs(items): close move-to-git-issues design after end-to-end verification
> ```
>
> When that PR is green and merged, the design is officially complete. Post a final summary to chat with: the closed-Issue URLs, the merged PR URL for the close-out doc edit, and any drift surfaced during Steps 1–4 that needs a follow-up backlog row (file via the new `mcp-server-surface-test-finding` template).
>
> Do not push, comment, or file Issues outside what these steps explicitly authorize. If you hit a real bug during the verification, file it via the new template (`/mcp-server-surface-test --auto-file` will do this) — that itself is end-to-end evidence.

---

## Notes for the operator (you, before pasting)

- **The full plan** that drove Rows 1–3 is at `C:/Users/daryl/.claude/plans/ready-to-start-looking-harmonic-toast.md` — the "End-to-end verification" section there is what this prompt operationalizes.
- **Skill auto-load on Claude Code Windows** historically requires the rename-step workaround per `~/.claude/projects/C--Windows-system32/memory/feedback_plugin_install_eperm.md`. If `/plugin update` errors, run `claude plugin install roslyn-mcp@roslyn-mcp-marketplace` from CLI and reapply the manual rename before pasting the prompt.
- **Self-hosted runner contention** — the runner lives on this same box. Never run `--full` (or full `dotnet test`) locally while a PR is queued. See `~/.claude/projects/C--Code-Repo-Roslyn-Backed-MCP/memory/feedback_self_hosted_runner_no_duplicate_local_run.md`.
- **Why this prompt also closes the design row** — the move-to-git-issues design has been "ready to ship" several times; the load-bearing definition of done is a status flip on the design note itself plus an audit-trail of the closed verification Issues. Pasting this prompt and letting it run all the way through Step 7 is the actual completion ritual.
