---
name: mcp-server-surface-test
description: "Consumer-facing audit of the Roslyn MCP server's live surface against a loaded C# repo. Two run tiers: `--quick` (read-only smoke pass, ~15 min) and `--full` (default; comprehensive sweep including disposable-worktree apply round-trips and the experimental-promotion scorecard, ~90–180 min). Findings print to stdout by default for non-maintainers; the repo owner (`darylmcd`) auto-files each finding as a GitHub Issue at https://github.com/darylmcd/Roslyn-Backed-MCP. Pass `--auto-file` to force-enable or `--no-auto-file` to force-disable. Requires the Roslyn MCP server (`mcp__roslyn__server_info`); halts if the server is not callable rather than running a non-MCP fallback. Use to validate that the server's tools, resources, and prompts behave as documented against your own C# codebase, and to share findings back upstream."
user-invocable: true
argument-hint: "[<target-repo-path>] [--quick | --full] [--output-mode=findings | fragments] [--auto-file | --no-auto-file] [--no-worktree] [--single-agent]"
---

# /mcp-server-surface-test $ARGUMENTS

Run a Roslyn MCP server audit against a loaded C# repo. The skill bundles two prompt files — `prompts/quick.md` for the bounded read-only tier and `prompts/full.md` for the comprehensive run — and routes between them based on `$ARGUMENTS`.

## Step 1 — Hard precondition: Roslyn MCP server must be callable

This skill is a **null-op without the Roslyn MCP server**. The audit's entire purpose is to exercise the server's live surface — without it, the run produces no audit-grade evidence.

1. Verify `mcp__roslyn__server_info` appears in your current tool surface and call it. The response must include `connection.state: "ready"`.
2. If the call fails, the tool is missing, or `connection.state` is `initializing` / `degraded` / absent, **stop and report**:

   > *"This skill requires the Roslyn MCP server (`mcp__roslyn__*` tools must be callable, `connection.state` must be `ready`). Start the server — for example `dotnet tool run roslynmcp` or ensure the plugin's stdio entry is active in your client config — confirm `mcp__roslyn__server_info` returns `ready`, then re-invoke this skill."*

   Do **not** substitute `Read`, `Grep`, `Bash: dotnet build`, or any other host-side fallback. There is no generic non-MCP audit fallback in this skill — a broken server precondition halts the run.

## Step 2 — Parse $ARGUMENTS and route to a tier prompt

`$ARGUMENTS` may include any combination of the following tokens, in any order. Tokens are space-separated.

### Target repo (default: current Claude Code session's repo root)

The audit needs a C# workspace to load via `mcp__roslyn__workspace_load`. By default the audit targets the **current session's repo root**. Override the target in two equivalent ways:

- `--target=<absolute-path-or-repo-root>` — explicit flag form.
- *(bare path)* — any single token in `$ARGUMENTS` that resolves to an existing directory containing a `.sln`/`.slnx`/`.csproj` file.

Resolution rules:

1. If both `--target=<path>` and a bare path appear, **stop and report the conflict** — pick one form. Do not silently prefer one over the other.
2. The resolved target path becomes the **audited repo root** throughout the prompt — *every* output path computed by the prompt (the audit report, the disposable worktree base, finding emission) anchors here, not at the agent session's CWD.
3. If the resolved path does not exist, contains no `.sln`/`.slnx`/`.csproj`, or is not a git working tree, **stop and report** — the audit cannot proceed without a loadable workspace and a tree the disposable-worktree step can branch from.
4. Pass the resolved target to the prompt's Phase 0 as the `workspace_load` argument; the prompt's *Isolation* row in the report header records the resolved target path.

### Tier flags

- *(no flag, default)* — `--full` is the default. Read `prompts/full.md` and run it phase by phase.
- `--quick` — read-only smoke pass. Read `prompts/quick.md` instead. Target runtime ≤15 minutes vs `--full`'s 90–180 minutes. No apply-mode mutations, no disposable worktree, no test runs, no `nuget_vulnerability_scan` (network-dependent).
- `--full` — explicit form of the default; routes to `prompts/full.md`.
- **Conflict:** `--quick` and `--full` together → stop and report; pick one.

### `--single-agent` (full tier only)

- *(no flag, default)* — the full tier dispatches phase groups to `audit-phase-runner` subagents per the prompt's *Phase 0.5: Subagent dispatch plan*. The orchestrator owns workspace lifecycle, the disposable worktree's `try/finally`, all report-file writes, and finding emission; subagents return compact structured summaries the orchestrator pastes in. This is the only way the `--full` tier achieves its 250+ tool-call coverage without burning through a single agent's context window.
- `--single-agent` — opt out of the dispatch plan and run every phase in the orchestrator's own context. Use only when (a) the host environment cannot spawn subagents, or (b) the operator wants a one-shot run and accepts that long phases may surface as `phase-failed-budget` in the coverage summary. The completion gate (no silent `skipped-budget` truncation) still applies — partial runs surface as P1 audit defects, not as quiet ledger entries.
- **Has no effect under `--quick`** (the quick tier is bounded enough to run in a single agent by design); reject the combination with a one-line message.

### `--no-worktree` (full tier only)

- *(no flag, default)* — full canonical run with the disposable-worktree apply pass exercised.
- `--no-worktree` — degraded mode for environments that genuinely cannot create a git worktree. Phase 6 sub-phases that require a worktree are marked `skipped-safety — --no-worktree`. The promotion scorecard still emits, but writer recommendations default to `needs-more-evidence` for any tool whose round-trip evidence depended on the disposable worktree. **Has no effect under `--quick`** (the quick tier already skips Phase 6); reject the combination with a one-line message.

### `--output-mode=findings|fragments` (both tiers)

Default: `findings` — Phase 19 emits stdout / GitHub Issues per the maintainer-aware routing below.

- `--output-mode=fragments` — Phase 19 instead emits one `<audited-repo-root>/backlog.d/<finding-id>.md` per actionable finding, for `/backlog-intake` to consume. Intended for maintainer-managed repos that participate in a `backlog.d/` ingestion pipeline. Bypasses `--auto-file` / `--no-auto-file` entirely — those flags have no effect under fragments mode (the file-system handoff IS the destination). The prose `.md` report still writes to the audited repo's `audit-reports/` directory; `/backlog-intake` reads the fragment's `source_audit` field to back-reference the prose report. See `prompts/full.md` Phase 19 *Fragments path* for the full contract.
- The `/mcp-server-stress` skill (maintainer-only repo-local alias) is a thin invocation of `/mcp-server-surface-test --output-mode=fragments` against the current Claude Code session's repo — it exists for muscle-memory continuity and to advertise the fragments-mode default for maintainer audits.

### `--auto-file` / `--no-auto-file` (findings mode only)

The auto-file default is **maintainer-aware**: the skill probes the operator's GitHub identity and auto-files when the operator owns the upstream repo. Findings always go to `https://github.com/darylmcd/Roslyn-Backed-MCP` regardless of the audited repo, since findings are about the MCP server itself and the audited repo is just a fixture. These flags have no effect under `--output-mode=fragments` — fragments mode skips the GitHub-Issues handoff entirely.

**Maintainer detection (run once per invocation, before Phase 19):**

Dot-source `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/lib/render-finding.ps1` and call `Test-IsMaintainer`. It wraps `gh api user --jq .login` and compares against the maintainer login derived from the single `$script:UpstreamRepo` constant — that one literal in `lib/render-finding.ps1` is the source of truth for "who is the maintainer" and "where do auto-filed Issues land." Any failure mode (`gh` missing, unauthenticated, network failure, login mismatch) returns `$false` and is treated as **not the maintainer**.

**Default (no flag):**

- **Maintainer detected** (`gh api user --jq .login` == `darylmcd`): auto-file each non-refused finding via `gh issue create` (same call as the explicit `--auto-file` path).
- **Otherwise**: print each finding to stdout as a ready-to-paste GitHub Issue body. The operator decides what to do with each.

**Explicit overrides:**

- `--auto-file` — force-enable auto-filing regardless of detected identity. Still subject to the `gh`-available + authenticated requirement (falls back to stdout-print with one warning line if either is missing); still subject to the P0/security refusal contract below.
- `--no-auto-file` — force-disable auto-filing. Always emit to stdout, even when the maintainer is detected. Use this for a dry-run on the maintainer's own machine.
- **Conflict:** `--auto-file` and `--no-auto-file` together → stop and report; pick one.

**Refusal contract — load-bearing pre-disclosure safeguard:** the skill does **not** call `gh issue create` for any finding whose `severity == P0` or whose `area == security`. Such findings print to stdout with this header line: `**SECURITY / P0 finding — DO NOT FILE PUBLICLY.** Escalate via GitHub security advisories at https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new`. The refusal applies regardless of detected identity, `--auto-file`, or `--no-auto-file`.

### Reject

Reject any token that is neither a valid target path, `--target=<path>`, `--quick`, `--full`, `--output-mode=findings`, `--output-mode=fragments`, `--no-worktree`, `--single-agent`, `--auto-file`, nor `--no-auto-file`. One-line message; ask the user to fix or drop the offending token. **Conflict:** `--auto-file` or `--no-auto-file` combined with `--output-mode=fragments` → emit a one-line warning explaining that the auto-file flag is ignored under fragments mode, then proceed with fragments mode.

## Step 3 — Mutation safety: disposable worktree (full tier, default mode)

The audit is **read-only against the audited repository's `main` branch**. Phase 6 of the full tier writes apply-mode mutations, but only inside a disposable worktree the prompt creates and tracks. The flow is:

1. Before any Phase 6 apply, the prompt creates a disposable branch + worktree at run start and records the path in the report header (the *Isolation* row). This is **mandatory** in default full-mode — Phase 6 cannot run against the audited repo's primary checkout.
2. Phase 6's preview → apply chains run against that disposable checkout. Applies are exercised as test fixtures of the apply-tool surface (preview→apply→revert round-trips, `compile_check` after apply, `build_workspace` + `test_run` after apply). The point is to exercise the write path of the MCP server, not to ship product changes.
3. The disposable worktree is torn down at run end via `dotnet build-server shutdown` followed by `git worktree remove --force` (the Windows lock-release sequence is mandatory; `dotnet build-server shutdown` releases `testhost.exe` / `VBCSCompiler.exe` locks on the worktree's `bin/` dirs). Teardown runs even on apply failure — the prompt wraps the Phase 6 chain in `try/finally` discipline.
4. The audited repo's `main` branch is **never** directly mutated. No PR is opened from the audit run. No commits land in the audited repo's history.

The `--no-worktree` flag opts into a degraded mode where the disposable worktree is skipped — see Step 2 for the contract and the report-header record requirement. The `--quick` tier always skips the disposable worktree (no apply-mode phases run at all).

## Step 4 — Execute the chosen prompt

- `--quick`: read `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/prompts/quick.md` and run it phase by phase.
- `--full` (default): read `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/prompts/full.md` and run it phase by phase.

Persist the audit draft after each phase as the prompt instructs — the canonical report path lives in the prompt's *Output Format* section: `<audited-repo-root>/audit-reports/<timestamp>_<repo-id>_mcp-server-surface-test.md`.

## Step 5 — Finding emission (both tiers)

After the audit phases complete, the prompt's final finding-emission phase walks every actionable finding (entries in *MCP server issues* or *Improvement suggestions* with a concrete fix sketch) and renders each through one shared envelope:

- **Stdout-print path** (non-maintainer default, or when `--no-auto-file` is passed): print each finding to stdout as a ready-to-paste GitHub Issue body. The render block is `## TITLE`, label list, and the structured body fields (`id`, `source-repo`, `severity`, `area`, `server-version`, `anchors`, `finding`, `repro`, `proposed-fix`).
- **Auto-file path** (maintainer default — `gh api user --jq .login` == `darylmcd` — or when `--auto-file` is passed): call `gh issue create --repo darylmcd/Roslyn-Backed-MCP --title <title> --label <area:X,severity:Y> --body-file <tempfile>` per finding, except those refused by the P0/security refusal contract (Step 2) and those filtered by the dedup pre-check (Step 5a). Refused findings still print to stdout with the security-advisory escalation banner.

## Step 5a — Dedup pre-check against existing GitHub Issues (auto-file path)

Before each `gh issue create` invocation, the auto-file path **must** query the issue tracker for a near-match and skip filing when one exists. This prevents the auto-filer from re-filing the same bug across audit runs, or from filing a finding that has already been promoted to `good first issue` via the maintainer-curated path. Without this gate, the auto-filer is the primary source of duplicate-issue noise (the gap was first observed on 2026-05-11 when the IT-Chat-Bot audit auto-filed a duplicate of an existing maintainer-promoted `goto_type_definition` issue).

For each finding about to be filed:

1. **Compute a search key** from the finding's title. The tool name is the most stable identifier — e.g., for the title `[audit] goto_type_definition throws for BCL/metadata-only types instead of returning structured no-source result`, the key is `goto_type_definition`. For findings that span multiple tools or skills, use the first tool name plus one or two distinguishing symptom keywords.
2. **Query existing issues:**
   ```bash
   gh issue list --repo darylmcd/Roslyn-Backed-MCP \
     --state all \
     --search "<search-key> in:title" \
     --json number,state,title,labels --limit 10
   ```
3. **Apply the dedup rules:**
   - **Title near-match on an OPEN issue** (search key matches AND at least 2 additional symptom keywords from the finding's title overlap with the existing issue's title): SKIP filing. Emit one line: `→ skipped: duplicate of #<N> (open) — <existing-title>`. Add the finding to the run's "deduped" audit-trail list.
   - **Title near-match on a CLOSED issue**: SKIP filing. Emit `→ skipped: previously resolved as #<N> (closed) — <existing-title>`. Do not refile; if the operator believes the finding represents a regression of a prior fix, they will re-open the existing issue manually.
   - **No match**: proceed with `gh issue create` as before.
4. **Refusal contract still applies first.** P0/security findings (per Step 2) are stdout-only regardless of dedup result — do not run the dedup query against the issue tracker for these findings.

The match threshold is intentionally conservative: better to skip a borderline case and emit a warning than to file a true duplicate. Operators reviewing the skipped-duplicate list can re-file manually if the dedup was wrong. The audit-trail entry for each skipped finding includes the candidate existing issue number so the operator can verify quickly.

## Operational notes

### Archiving old audit reports — `scripts/archive-old-reports.ps1`

Reports written to the `audit-reports/` directory accumulate over time. The skill ships a small PowerShell wrapper at `${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1` that moves `*.md` files older than N days (default 30) into a year-stamped `archive/<YYYY>/` subdirectory. The reports directory path defaults to `audit-reports` and can be overridden via `-ReportsRelativePath`.

Invocation (any shell with `pwsh` on path):

```bash
# Preview the archive plan without mutating anything.
pwsh -NoProfile -File ${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -DryRun

# Archive reports older than 60 days under the default reports directory.
pwsh -NoProfile -File ${CLAUDE_PLUGIN_ROOT}/skills/mcp-server-surface-test/scripts/archive-old-reports.ps1 -OlderThanDays 60
```

Behavior contract:

- **Pinned filenames are never archived** — `README.md` stays in place regardless of age.
- **Idempotent** — running twice is safe. The destination year-subdirectory is created on demand.
- **Read-only when `-DryRun` is set.**
- **Independent of the Roslyn MCP server** — the archive script does not require MCP tooling.

The script is invoked manually (no automatic scheduler).

## Hard rules

- **Server-required.** No generic non-MCP fallback exists. If `mcp__roslyn__server_info` is not callable or `connection.state` is not `ready`, halt.
- **Read-only against `main`.** All apply-mode mutations confine to the disposable worktree the prompt creates. Never push or merge from inside this skill.
- **No PR.** This skill produces an audit report, not a refactor PR. Phase 6 mutations are exercised as apply-tool fixtures inside the disposable worktree and torn down at run end.
- **Cite, don't summarize.** Every finding must reference a concrete file:line and a tool call — no abstract claims.
- **Disposable-worktree teardown is mandatory** (full tier, default mode). The Phase 6 apply chain runs inside `try/finally` so teardown executes even on apply failure. `dotnet build-server shutdown` always precedes `git worktree remove --force` on Windows.
- **P0 / security findings are never auto-filed.** The refusal applies whether the operator is the detected maintainer, passed `--auto-file`, or passed `--no-auto-file` — those findings are stdout-only with the security-advisory escalation banner.
