# move-to-git-issues - current design for external audit findings

<!-- purpose: Current design note for moving the audit-finding sharing path toward GitHub Issues while keeping the maintainer-local fragment workflow. -->
<!-- scope: in-repo -->
<!-- status: completed - rows 1-3 shipped (PRs #591, #592, #593) and released as v1.35.1 (PR #595); end-to-end verification passed 2026-05-10; row 4 deferred per design note -->

**Created:** 2026-05-08  
**Last reconciled:** 2026-05-09  
**Trigger:** Final design conversation after the 20260507T182128Z backlog sweep, then revised after empirical evidence from the 2026-05-08 audit run.

---

## Current decision snapshot

The current design is **not** "replace `backlog.d/` with GitHub Issues." That earlier direction was superseded.

The accepted shape is:

1. **Maintainer intake stays local.** `/mcp-server-stress` continues to emit one `backlog.d/<finding-id>.md` fragment per actionable finding for maintainer-run audits. `/backlog-intake` continues to consume those fragments locally. This path is fast, offline-capable, and already validated by the 2026-05-08 audit run.
2. **Maintainer-found accepted issues may also go public.** After local triage, valid maintainer-found findings can be filed as GitHub Issues for visibility, project-health signaling, and contributor onboarding. Do not dump every raw fragment into GitHub; curate findings first, then file issues that are real, understandable, and worth tracking publicly.
3. **Consumer sharing becomes additive.** Consumers get a clean GitHub Issue path for findings they choose to share with `darylmcd/Roslyn-Backed-MCP`. In v1, the low-risk version is print-ready issue bodies plus an opt-in `--auto-file` path when `gh` is already authenticated. No proxy, GitHub App, telemetry endpoint, or silent filing in v1.
4. **The shipped skill target name is `mcp-server-surface-test`.** Forward-looking surfaces use that name: skill folder, issue template, labels, docs, and commands. Historical `audit-deep` and `mcp-server-stress` references stay only where they describe already-shipped history.
5. **The shipped skill must be generic.** Moving `.claude/skills/mcp-server-stress/` into `skills/mcp-server-surface-test/` is not a mechanical move. Shipped `./skills/**/SKILL.md` files cannot reference repo-only markers — the canonical banned-pattern set checked by `eng/verify-skills-are-generic.ps1` is `state.json`, `backlog-sweep`, `schemaVersion`, `ai_docs/`, `backlog.md`, `eng/`, `just verify-`, `Directory.Build.props`, and `BannedSymbols.txt`. Note that `backlog.d` is **not** banned: consumer-side fragments may legitimately land at `<consumer-repo>/backlog.d/`. Split consumer-generic behavior from maintainer-only repo workflow as needed.
6. **Two run tiers are enough for v1.** `--quick` is the consumer-facing read-side tier; `--full` is the maintainer/default comprehensive tier. Defer a middle tier until there is demand.
7. **Use one finding envelope.** The existing fragment schema is the base contract: `id`, `source_audit`, `source_repo`, `severity`, `area`, `anchors`, plus a new `server-version` field. Enrich the audit report with a structured `workspace-shape:` block instead of duplicating workspace metadata on every finding.
8. **Public docs should be slim but explicit.** Add enough README and issue-template content that a consumer understands what `/mcp-server-surface-test` does, what each tier covers, expected runtime, privacy/safety, and how to share a finding. Include that maintainer-found issues are filed publicly to expose project quality work and create contributor entry points.
9. **Community scorecard aggregation is stretch work.** Do not extend `publish-preflight` or `eng/aggregate-promotion-scorecards.ps1` for issue-comment input until real community scorecard data exists.

## Recommended row set

These are design candidates for a future planning pass, not open backlog rows yet.

| Row | Scope | Notes |
|---|---|---|
| 1. Ship as new generic skill at `skills/mcp-server-surface-test/` | Split the maintainer prompt into a generic-safe consumer half (under `skills/`) and a maintainer-only overlay (kept under `.claude/skills/`); update references and tests. | The shipped skill has never lived under `./skills/`, so this is a first-time ship, not a relocate. The genericity split is the bulk of the work — the current maintainer prompt has multiple banned-pattern hits, so this is not just `git mv`. |
| 2. Tiered runs + shared finding envelope | Add `--quick` / `--full`, stamp `server-version`, enrich the audit report with `workspace-shape:`, and render findings through one fragment/issue envelope. | Keep `print` as the consumer default. `--auto-file` may use `gh issue create` only when explicitly requested and authenticated. Maintainer `--auto-file` should supplement local fragment emission, not replace it. |
| 3. Slim consumer documentation + issue template | Add README guidance and `.github/ISSUE_TEMPLATE/mcp-server-surface-test-finding.yml`. | Keep the tone factual: what the skill does, what it does not do, tier cost, privacy, where a finding goes, and how public issues can become contribution entry points. |
| 4. Community scorecard input | Optional issue-comment or issue-body scorecard aggregation. | Stretch only. Ship after there is actual community scorecard evidence. |

No v1 row should replace `/backlog-intake`'s fragment flow. Add issue intake automation only after public issue volume proves it is useful.

## Public contributor funnel

GitHub Issues should become the public-facing tracker for accepted, contributor-relevant work. This is separate from the local agent execution backlog.

Recommended flow:

1. A maintainer audit emits local fragments.
2. The maintainer triages fragments through `/backlog-intake`.
3. Valid findings that are worth tracking publicly get GitHub Issues.
4. `ai_docs/backlog.md` keeps or gains the planner-ready row with a link to the GitHub Issue.
5. Implementation PRs use `Closes #<issue>` so the public issue history shows discovery, fix, and closure.

Issue quality matters. A maintainer-filed issue should include:

- short title with the affected tool, prompt, or workflow;
- concise repro or audit evidence;
- expected vs. actual behavior;
- likely fix area or anchor paths when known;
- acceptance criteria that an outside contributor can verify;
- labels for area and severity.

Use `help wanted` only when an outside PR would be welcome. Use `good first issue` only for genuinely bounded work that does not require deep Roslyn MCP internals, release-managed files, or broad workflow knowledge.

`ai_docs/backlog.md` remains the agent-ready execution queue. GitHub Issues are the public contribution and visibility surface, not the replacement for the structured backlog contract.

## Superseded decisions

| Superseded idea | Current replacement |
|---|---|
| Pivot all actionable findings away from `backlog.d/` and into GitHub Issues. | Keep `backlog.d/` for maintainer audits; add Issues as a consumer-sharing path. |
| Single path through GitHub Issues with `triaged-by-maintainer` as the maintainer shortcut. | Two paths are intentional: local fragments for maintainer intake, issues for outside contributors. |
| Sunset `eng/stage-review-inbox.ps1` fragment discovery and `/backlog-intake` fragment walking. | Keep both. They are the validated maintainer path. |
| Require an 8-field issue schema (`audit-run-id`, `finding-id-within-run`, `audited-workspace-shape`, structured `repro-context`, etc.). | Use the existing 6-field fragment schema plus `server-version`; put workspace shape in the audit report. Defer extra fields until triage asks for them. |
| Build a proxy, GitHub App, or telemetry endpoint for seamless filing in v1. | Defer hosted infrastructure until there is volume signal. Use print-ready bodies and optional `gh` filing first. |
| Full consumer-facing docs package with deep-dive doc, triage SLA doc, csproj metadata, and CONTRIBUTING edits. | Start with README plus issue template. Add more surfaces only when issue volume or user confusion justifies them. |
| Block the pivot behind the 9 findings from the 2026-05-08 audit run. | That audit batch has already been mostly shipped/reconciled. Route new work from the current `ai_docs/backlog.md`, not this historical note. |
| Keep `audit-deep` or `mcp-server-stress` as forward-looking names. | Use `mcp-server-surface-test` for all new public surfaces. Preserve older names only in historical references. |
| Keep maintainer-found issues only in local fragments for speed. | Keep local fragments for intake speed, but file curated accepted findings publicly when visibility or contribution value is worth it. |

## Implementation constraints

- Follow `AGENTS.md` shipped-skill packaging rules before moving anything under `./skills/`.
- Run `./eng/verify-ai-docs.ps1` for doc-only changes.
- If shipped skills change, `verify-ai-docs.ps1` also invokes `eng/verify-skills-are-generic.ps1`; treat that as a required gate, not a cleanup task.
- If code or packaged skill behavior changes, run the release validation path from `CI_POLICY.md`.
- Do not add pivot rows directly to `ai_docs/backlog.md` until they are sized into bounded initiatives.

## Remaining questions

These are the only questions worth carrying into a future planning or sanity-check pass:

1. Is `--quick` enough for consumers, or should there also be a very small `--smoke` tier?
2. Exactly how should the issue template render the shared finding envelope without requiring fields the producer does not already emit?
3. Which labels define the initial contributor funnel (`help wanted`, `good first issue`, area, severity), and which labels should wait for real issue volume?
4. Should the rename/relocate ship as its own row before the tier and issue-rendering work? Current lean: yes, to reduce review churn.

## References

| Path | Role |
|---|---|
| `ai_docs/plans/20260507T182128Z_backlog-sweep/plan.md` | Sweep that introduced the maintainer-only audit redesign and fragment pattern. |
| `ai_docs/plans/20260508T171439Z_backlog-sweep/plan.md` | Follow-up sweep that shipped the 2026-05-08 audit findings and changed priority context. |
| `.claude/skills/mcp-server-stress/SKILL.md` | Current maintainer-only skill entry point. |
| `.claude/skills/mcp-server-stress/prompts/maintainer-overlay.md` | Maintainer-only superset prompt (renamed from `prompts/prompt.md` when Row 1 shipped); retains repo-coupled phases the shipped consumer prompt does not have. |
| `.claude/skills/backlog-intake/SKILL.md` | Maintainer intake flow that consumes `backlog.d/` fragments. |
| `ai_docs/items/backlog-d-fragment-schema.md` | Canonical fragment schema; base for the shared finding envelope. |
| `ai_docs/backlog.md` | Current open-work source of truth. |
| `.github/ISSUE_TEMPLATE/` | Existing GitHub issue-template folder for the future `mcp-server-surface-test-finding.yml`. |
| `AGENTS.md` | Shipped-skill genericity and bootstrap rules. |
| `CI_POLICY.md` | Validation and merge-gating policy. |

## Verification

End-to-end verification per `ai_docs/items/move-to-git-issues-end-to-end-verification.md` completed on **2026-05-10**.

| Step | Mode | Result |
|---|---|---|
| 0. Seed labels | real, idempotent | 7 area + 3 severity labels confirmed at `darylmcd/Roslyn-Backed-MCP` |
| 1. `/mcp-server-surface-test --quick` against `C:/Code-Repo/DotNet-Firewall-Analyzer` | end-to-end | Audit report at `audit-reports/20260510T050225Z_firewallanalyzer_mcp-server-surface-test.md`; no `backlog.d/` writes; no `gh` invocations; Phase 19 = `**N/A — no actionable findings**`; wall-clock ≈2 min (budget ≤15 min). |
| 2. Synthesized consumer Issue via shared renderer | synthesized + real `gh issue create` | Filed [#598](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/598) `verify-end-to-end-quick-auto-file-probe`; labels `area:docs` + `severity:P3`; closed in Step 6. |
| 3. `/mcp-server-surface-test --full` | **skipped** | Per `feedback_self_hosted_runner_no_duplicate_local_run.md` — runner contention. |
| 4. Synthesized maintainer publish path | synthesized + real `gh issue create` + real fragment delete | Filed [#599](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/599) `verify-end-to-end-publish-probe`; sibling-repo fragment at `dotnet-firewall-analyzer/backlog.d/verify-end-to-end-publish-probe.md` consumed and deleted (directory removed); backlog row was added then reverted within the verification session (no commit on `main`); closed in Step 6. |
| 5. Refusal contract | renderer-only, no `gh` | `Test-FindingShouldRefusePublicFile` and `Render-FindingIssue.refusedPublic` both returned `True` for `severity=P0` and for `area=security`; `DO NOT FILE PUBLICLY` banner matched in both rendered bodies. |
| 6. Cleanup | real | Both Issues closed with `not planned` reason; synthetic backlog row reverted; sibling-repo `backlog.d/` empty. |

Renderer parity verified directly by diffing the bodies of [#598](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/598) (consumer auto-file path) and [#599](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/599) (maintainer publish path) — identical envelope shape and field ordering, which is the load-bearing contract Row 2 was designed to enforce.
