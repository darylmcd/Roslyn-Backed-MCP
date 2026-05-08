# move-to-git-issues — design seed for the audit-finding pivot

<!-- purpose: Captures the architectural rethink that surfaced at the end of the 20260507T182128Z backlog sweep — pivoting audit findings away from the local `backlog.d/` fragment pattern toward GitHub Issues so the skill can ship to consumers. Pre-brainstorm seed; not yet a backlog row. -->
<!-- scope: in-repo -->
<!-- status: pre-brainstorm — design discussion captured, no rows planned yet -->

**Created:** 2026-05-08
**Trigger:** Final design conversation in the 20260507T182128Z backlog sweep session, after all 6 initiatives merged.
**Next step:** A `/brainstorming` session that uses this doc as input to produce a sized backlog row set.

---

## TL;DR

The 20260507T182128Z sweep moved `/mcp-server-stress` (formerly `/audit-deep`) from the shipped plugin surface (`skills/audit-deep/`) to maintainer-only (`.claude/skills/mcp-server-stress/`) and built a `<audited-repo>/backlog.d/` fragment pattern for cross-repo audit findings. That pivot rationally followed from the assumption *"only the maintainer audits the server."* That assumption is wrong: the entire point of the audit is to find server bugs, and consumers running it against the C# codebases they have access to would dramatically widen the test surface. The right architectural fix is to **send actionable findings to GitHub Issues on the server's repo (`Roslyn-Backed-MCP`)**, which lets the skill go back to the shipped surface and lets consumers contribute findings. Most of the recently-shipped sweep survives; the relocate (inits 2+3) and the `backlog.d/` fragment pattern (init 5) are the parts that become partially obsolete.

---

## What just shipped (the 20260507T182128Z sweep)

Plan: `ai_docs/plans/20260507T182128Z_backlog-sweep/plan.md`. Six PRs landed, five backlog rows closed:

| PR | Initiative | Closes row |
|---|---|---|
| #547 | `audit-deep-collapse-modes` | `mcp-server-stress-single-mode` |
| #548 | `mcp-server-stress-relocate` | (paired with #549) |
| #549 | `mcp-server-stress-update-external-refs` | `audit-deep-relocate-and-rename` |
| #550 | `extract-skills-audit-from-server-stress` | `extract-skills-audit-from-server-stress` |
| #551 | `backlog-d-fragment-pattern` | `backlog-d-fragment-pattern` |
| #552 | `per-repo-promotion-scorecard` | `per-repo-promotion-scorecard` |

Plus #553 (plan-completion chore), #554 (changelog fragment for #545), and #545 (release-cut Step 6 fix) since v1.34.2.

Theme of the sweep: **maintainer-only audit-skill redesign.** Every initiative was sized around that frame.

## The architectural insight

We rationalized the move-to-maintainer-only by claiming *"a normal `roslyn-mcp@roslyn-mcp-marketplace` consumer has no use for a skill that audits this repo's own surface, scorecard JSON, and `skills/*/SKILL.md` inventory."* That's true *if findings have to land back in this repo's local `backlog.md`* — which forces the skill to assume the auditor has commit access to `Roslyn-Backed-MCP`. The producer→consumer model we built around `<audited-repo>/backlog.d/` and `/backlog-intake` is internally consistent for the maintainer use case but doesn't generalize.

**The shape that does generalize is GitHub Issues.** Issues already have:
- Severity (labels)
- Dedup (search before file)
- Comments (clarification, follow-up, repro updates)
- Assignees (triage)
- Repro metadata (markdown body + attached files)
- Closed-via-PR linking
- Cross-repo references

We were re-implementing a partial version of this in YAML frontmatter. The Issues-based version of the same flow is:

| | Old (just-shipped) | New (proposed) |
|---|---|---|
| Producer | `/mcp-server-stress` (maintainer-only) | `/mcp-server-stress` (shipped, runs anywhere) |
| Per-finding scratch | `<audited-repo>/backlog.d/<id>.md` | GitHub Issue on `Roslyn-Backed-MCP` (labeled `audit-deep`) |
| Consumer | `/backlog-intake` walks sibling repos, dedupes, deletes consumed fragments | `/backlog-intake` lists `audit-deep`-labeled issues, triages, accepts/rejects, converts accepted → backlog rows |
| Master record | `Roslyn-Backed-MCP/ai_docs/backlog.md` | Same — populated only by triaged-and-accepted issues |

## What survives from the recent sweep

Substantively right and stays:

- **Mode collapse (init 1)** — single canonical run with always-disposable-worktree + always-scorecard. Independent of where output goes.
- **Disposable-worktree Phase 6 (init 1)** — applies-as-test-fixtures discipline. Audited repo's `main` is never mutated. Independent of output target.
- **Plugin-skill audit moved to /surface-audit (init 4)** — that was a separate concern (static catalog vs runtime execution). Stays where it is.
- **Per-repo scorecard at `<audited-repo>/ai_docs/audit-reports/_latest-promotion-scorecard.json` (init 6)** — local evidence file. What changes is whether the audit *also* posts a summary as an issue comment for community-quorum aggregation.
- **`eng/aggregate-promotion-scorecards.ps1` (init 6)** — survives. Its input may grow to include issue-comment scorecards for community runs in addition to local file scrapes for maintainer runs.

Roughly **~50% of inits 2+3** also survives even if we reverse the relocate: the cleanup of Phase 16b coupling, name conflicts, external references in `publish-preflight/SKILL.md` and `audit-phase-runner.md` is real cleanup that doesn't unwind.

## What becomes partially obsolete

- **Relocate to `.claude/skills/mcp-server-stress/` (inits 2, 3)** — needs to reverse. Skill goes back to shipped `skills/mcp-server-stress/`. The shipped surface we'd put back is *cleaner* than the original because of the cleanup work in init 3 (which still survives semantically).
- **`backlog.d/` fragment pattern (init 5)** — the cross-repo emit-then-consolidate flow becomes redundant once findings go to issues. Specifically:
  - **Survives:** the fragment schema at `ai_docs/items/backlog-d-fragment-schema.md` — repurposes naturally as the **issue-body template** (frontmatter fields map onto issue metadata: `severity:` → label, `area:` → label, `anchors:` → markdown bullets, body stays as-is).
  - **Sunsets:** the discovery + delete logic in `eng/stage-review-inbox.ps1` and `/backlog-intake`'s fragment walk. Replaced by `gh issue list --label audit-deep` + maintainer triage flow.
  - **Stays as fallback:** local fragment emit may still be useful as `--target-output=fragments` for the maintainer's own multi-repo audit if `gh` isn't authenticated. See *Fallback story* below.

## The new shape (proposed)

```
/mcp-server-stress  (shipped at skills/mcp-server-stress/, invocable by any plugin consumer)
   │
   ├─ Loads C# workspace via Roslyn MCP (audited repo X — anywhere)
   ├─ Runs full audit including disposable-worktree Phase 6
   ├─ Writes prose evidence file:    <X>/ai_docs/audit-reports/<ts>_<repo>_mcp-server-audit.md
   ├─ Writes scorecard:              <X>/ai_docs/audit-reports/_latest-promotion-scorecard.json
   ├─ For each actionable finding:   gh issue create --repo darylmcd/Roslyn-Backed-MCP \
   │                                   --label audit-deep,severity:P0..P3,area:tools|... \
   │                                   --title "<title>" --body "<schema-formatted body>"
   └─ Exits with summary + list of created issue URLs
                  │
                  ▼
   GitHub Issues on Roslyn-Backed-MCP   ← producer-agnostic findings queue
                  │
                  ▼
/backlog-intake   (run from Roslyn-Backed-MCP by maintainer)
   │
   ├─ Lists `audit-deep`-labeled open issues (`gh issue list`)
   ├─ Triages: accept (→ backlog row), reject (→ close with comment), defer (→ keep open)
   ├─ For accepted: drafts row in ai_docs/backlog.md, adds "Closes #<issue>" reference
   └─ Issue stays open until the corresponding backlog row ships;
       PR closing the row uses "Closes #<issue>" trailer, GitHub auto-closes issue.
```

The audited repo X is never mutated except for its local `ai_docs/audit-reports/` (prose evidence + scorecard). No `backlog.d/`. No commit access required to anyone else's repo. The auditor only needs `gh` authenticated against `Roslyn-Backed-MCP` (or a fallback path; see below).

## Real costs / open design questions for the brainstorm

### 1. Auth + fallback story

**Problem:** filing issues needs `gh auth status` clean against `Roslyn-Backed-MCP` or a PAT with `repo:write` scope. Most plugin consumers won't have either.

**Proposed mitigation: `--issue-mode` flag with three values.**

| Mode | Behavior |
|---|---|
| `print` (default for community runs?) | Audit finishes, prints ready-to-paste issue body for each finding. User files manually if/when they want. No auth required. |
| `create` | Calls `gh issue create` directly. If `gh` is unavailable or auth fails, log a clear warning and fall back to `print` mode for that finding. |
| `preview` | Dry-run. Prints what `create` *would* do, doesn't file. Useful for the maintainer's first run on a new audited repo. |

**Open question:** which is the right default? `print` is safer for first-time consumers (no auth surprises), `create` is more useful for maintainer runs. Possibly `auto` — defaults to `create` if `gh` works, falls back to `print` silently. Brainstorm should land this.

### 2. Spam risk

**Problem:** random consumer audits could noise the tracker.

**Proposed mitigations (need to pick):**
- **Severity gating.** Only file P0/P1 by default; require `--include-p2-p3` for the noisier classes.
- **Confidence threshold.** Audit emits a `confidence:high|medium|low` field per finding; `--min-confidence=high` is the consumer-default. Maintainer runs with `--min-confidence=low` to see everything.
- **Rate limit.** No more than N issues per audit run; if there are more findings, summarize the rest in a single rollup issue.
- **Issue template.** Required fields: `server-version`, `platform`, `audited-workspace-stats` (project count, LOC). Forces enough metadata that triage is fast.
- **`audit-deep` label as a triage filter.** Maintainer's tracker dashboard filters on this label.

Brainstorm should pick the combo. My instinct: confidence threshold + required template + rate limit, no severity gating (P2/P3 are often the most actionable patterns and we shouldn't hide them).

### 3. Sunset window for the existing flow

**Problem:** the maintainer (you) currently audits sibling repos using the just-shipped `backlog.d/` pattern. Switching cold to issues means more friction (auth, label hygiene) for a workflow that was working.

**Proposed:** keep the local-fragment path as a fallback (`--target-output=issues|fragments|both`, default `both` during the transition window, eventually `issues`). The maintainer's own multi-repo audit doesn't regress. Sunset the fragment path after — say — 3 months of community use.

### 4. Issue triage discipline

**Problem:** issues filed by audits accumulate. Without explicit triage workflow, the tracker fills with stale `audit-deep`-labeled issues forever.

**Proposed contract:**
- Every audit-filed issue has the `audit-deep` label and a `from-audit:<audit-id>` reference.
- Triage SLA: maintainer reviews `audit-deep` issues weekly, decides accept/reject/defer.
- Accepted: backlog row created, issue stays open with `triaged:accepted` label; closed when the row ships via "Closes #N" PR trailer.
- Rejected: closed with maintainer comment explaining why. Optionally adds `triaged:not-actionable` label.
- Deferred: stays open with `triaged:defer` + a comment explaining what evidence/condition would unblock acceptance.

This needs to be written down somewhere — probably `docs/AUDIT_FINDINGS.md` or appended to `CONTRIBUTING.md`. Brainstorm should land where.

### 5. Cross-forge consumers

**Problem:** what about consumers using GitLab, Azure DevOps, etc.? They can still run the audit against their workspaces, but `gh issue create` against `Roslyn-Backed-MCP` only works on GitHub.

**Proposed:** the audit's issue target is **always the server's repo** (`darylmcd/Roslyn-Backed-MCP`), not the audited repo. Findings are about the server, not about the audited code, so they belong in the server's tracker regardless of where the audited code lives. Consumers on non-GitHub forges still need to file against `Roslyn-Backed-MCP`'s GitHub tracker — which they likely have read access to even without contributing. Worst case: they use `print` mode and paste manually.

### 6. Per-repo scorecard quorum with community evidence

**Problem:** init 6's `eng/aggregate-promotion-scorecards.ps1` aggregates scorecards across configured sibling repos owned by the maintainer. Community runs would produce scorecards in random workspaces the maintainer doesn't have access to.

**Proposed:** community runs optionally post their scorecard as an **issue comment** (or a structured comment on a dedicated `promotion-scorecard` rolling issue) so the maintainer can aggregate without needing access to the auditing workspaces. The aggregator script grows a mode that pulls from `gh issue` body/comments in addition to filesystem-local scorecards.

This is a stretch goal; not blocking the main pivot.

### 7. Issue-creation rate / GitHub API limits

**Problem:** a thorough audit can produce 20+ findings. Filing 20 issues at once via `gh` is fine for personal-PAT rate limits but worth noting. 100+ findings across a community run set is more concerning.

**Proposed:** rate-limit gate per run (see #2). Don't pre-engineer for the 100+ case until it's observed.

## Net cost estimate (rough — for brainstorm sizing)

Probably 4–6 backlog rows for the pivot. Sketch:

1. **Restore shipped skill location** — reverse the maintainer-local relocate. Mostly mechanical (git mv `.claude/skills/mcp-server-stress/` → `skills/mcp-server-stress/`), update test paths back, update external refs back to shipped names. The cleanup-content from init 3 stays. ≤4 prod files + 3 tests.
2. **GitHub Issues output mode** — add `--issue-mode=create|preview|print`, issue-body template (probably reuses `ai_docs/items/backlog-d-fragment-schema.md`'s schema, repurposed), label scheme, rate limit. Touches the audit prompt and adds a small helper script. 3-4 prod files + 1 test.
3. **`/backlog-intake` reshape** — replace fragment-walk with `gh issue list --label audit-deep` + triage flow. The fragment-walk path stays as a degraded-mode fallback (or is moved to a separate `--input=fragments|issues` flag) until sunset. 2-3 prod files + 1 test.
4. **Issue templates + label scheme + maintainer triage doc** — `.github/ISSUE_TEMPLATE/audit-finding.yml`, label definitions in repo settings (manual or via `gh label create`), triage doc at `docs/AUDIT_FINDINGS.md`. ≤4 prod files, 0 tests.
5. **`/publish-preflight` Step 8 + scorecard aggregator extension** — optional issue-comment input for community-quorum scorecards. Stretch — split out as a separate row.
6. **Sunset and cleanup** — after community use validates the pattern, remove the `backlog.d/` walk in `/backlog-intake`, remove `eng/stage-review-inbox.ps1`'s fragment-discovery branch, deprecate the `--target-output=fragments` flag. Final cleanup row, schedule for v1.36 or later.

Roughly the same scope as the sweep we just shipped. Some of it is reversal of recent work, which is going to feel weird in the CHANGELOG — the entries should be honest about it.

## What probably triggers the brainstorm

A `/brainstorming` session should produce:

1. **Decisions on the open questions in section "Real costs / open design questions"** — particularly auth fallback default (`auto` vs `print`), spam-mitigation combo, sunset window.
2. **Sized backlog rows** for the 4–6 initiatives above, ready for `/backlog-sweep:plan`.
3. **A short rationale doc** (`ai_docs/items/move-to-git-issues.md` updated in place, or a sibling design note) capturing the final design choices so future sessions don't re-litigate.

The brainstorm should NOT:
- Write code.
- Touch `ai_docs/backlog.md`.
- Start a backlog-sweep.

That's all subsequent steps.

## References (every artifact this discussion touched)

### Recently shipped (state at 2026-05-08)

- `ai_docs/plans/20260507T182128Z_backlog-sweep/plan.md` — the sweep that motivated the rethink.
- `ai_docs/plans/20260507T182128Z_backlog-sweep/state.json` — final state, `completed: true`.
- `ai_docs/plans/20260507T182128Z_backlog-sweep/review.md` — review-pass artifact.
- `.claude/skills/mcp-server-stress/SKILL.md` — current home of the skill (proposed to move back to shipped).
- `.claude/skills/mcp-server-stress/prompts/prompt.md` — the ~82KB audit body. The Phase 19 fragment-emit step is the section that gets rewritten to file issues instead.
- `.claude/skills/mcp-server-stress/scripts/archive-old-reports.ps1` — survives, no change needed.
- `.claude/skills/backlog-intake/SKILL.md` — has the fragment-walk logic from init 5; gets reshaped.
- `.claude/skills/publish-preflight/SKILL.md` Step 8 — uses the scorecard aggregator; may grow issue-comment input.
- `.claude/skills/promote-tier/SKILL.md` — accepts aggregated input format from init 6.
- `.claude/skills/surface-audit/SKILL.md` — owns the static SKILL.md audit absorbed from init 4. Independent of this pivot.
- `eng/aggregate-promotion-scorecards.ps1` — survives, may grow input modes.
- `eng/stage-review-inbox.ps1` — has the fragment-discovery branch from init 5; gets sunset.
- `ai_docs/items/backlog-d-fragment-schema.md` — the fragment schema; repurposes as issue-body template.
- `tests/RoslynMcp.Tests/Skills/McpServerStressSkillFrontmatterTests.cs` — tests the skill's frontmatter and tool references; needs path update when skill moves back to shipped.
- `tests/RoslynMcp.Tests/Skills/AggregatePromotionScorecardsScriptTests.cs` — tests the aggregator; may grow cases for issue-comment input.

### Repo-wide context

- `ai_docs/backlog.md` — master backlog (post-pivot, populated only by triaged-and-accepted issues).
- `ai_docs/workflow.md` — workflow conventions; the triage discipline doc may be added here or sibling.
- `ai_docs/prompts/backlog-sweep-addenda.md` — `changelog_convention: fragment` etc. The same fragment-then-consolidate pattern this pivot leaves behind for `backlog.d/`.
- `changelog.d/` — the changelog convention that inspired the (now-being-replaced) `backlog.d/` pattern. Stays.
- `CHANGELOG.md` — needs honest entries about the reversal when the pivot lands ("undoes part of v1.34.x's `mcp-server-stress-relocate`").

### Discussion threads / sessions

- The 2026-05-07 sweep planning + review + execute conversation (this session). Key turns:
  - Initial design conversation that produced the 5-row sweep (mode collapse, relocate, fragment pattern, scorecard).
  - Post-ship reflection on `/mcp-server-stress` invocation: "would this work as a global skill so consumers can run it from their own repo?" → led directly to the issues pivot.
  - Architectural question: "if this is repo-specific, why are we writing fragments to the remote repo and not the local?" → exposed the hidden assumption "only the maintainer audits."
- This very file. Future brainstorm sessions should read it first.

### External / reference patterns

- The `changelog.d/` + `/bump` pattern (this repo) — the *good* part of the fragment-then-consolidate model. Producer scope = single PR, consumer = release-cut. Stays.
- GitHub Issues + `Closes #N` PR trailer pattern — standard OSS contribution flow.
- `gh issue list --label <name>` and `gh issue create` — primitives the issues-mode skill uses.

---

## User feedback (2026-05-08, end of capturing session)

After reading the initial draft above, the user added course-corrections. These are decisions/leans, not just observations — incorporate them into the brainstorm starting position rather than re-litigating from scratch.

### (1) Operator friction on consumer issue filing — `print` mode is not viable

Quote: *"I dont think printing them is viable, i think a lot of issues would get lost in that process just due to the friction required. there is no real human thought around the mcp server automatically filing the issue and no real driving concern for most consumers to go through the effort of the copy and past to help us make this server better (even though they do benefit in the end). All that being said I would like to think about and find a SECURE way to allow the creation of the issues to be as seemless and pain free as possible so we get the most actionable issues as possible."*

**Implication:** the `--issue-mode=print` fallback is not the answer for consumers. If consumers have to copy-paste from terminal to GitHub web UI, the data never arrives in the tracker — friction wins, the use case dies. The whole pivot's value (community findings) hinges on file-issues-without-friction. So **seamless secure filing IS the design problem to solve**, not a nice-to-have.

**Design directions worth exploring in the brainstorm:**

| Approach | How it works | Trade-offs |
|---|---|---|
| **GitHub App (canonical)** | Maintainer publishes a GitHub App; consumer installs it on `Roslyn-Backed-MCP` (one-click "Install on this repo"); audit uses the installation token from the consumer's local config to file. | Cleanest; per-consumer scoped tokens; revocable per consumer. Higher setup cost (App registration, hosting the App's OAuth dance). Best long-term shape. |
| **Server-side proxy with maintainer PAT** | Small Cloudflare Worker / Azure Function at e.g. `audit.roslynmcp.dev/file-issue`; accepts POST with finding payload; validates schema/signature/rate; files issue using maintainer-held PAT (never leaves the proxy); returns issue URL. | Zero consumer auth; maintainer pays hosting (negligible at low volume); spam target = the endpoint, not the repo directly; rate-limit + per-IP throttling at the proxy; PAT lives in one place. Needs proxy uptime. |
| **`gh` if present, proxy fallback** | Skill detects `gh auth status`; if authenticated, uses `gh issue create` directly (consumer's own auth); if not, falls back to proxy. | Best of both worlds for consumers who already have `gh`. Two code paths to maintain. |
| **Anthropic-style telemetry endpoint** | The skill POSTs findings to an endpoint owned by the maintainer; endpoint can throttle, dedupe, even pre-triage before filing. | Maximum control; overkill for early-stage; possibly the right end-state if community runs become high-volume. |

**Lean from this conversation:** server-side proxy is probably the right starting answer. It gives consumers zero-friction filing, keeps the auth secret in one maintainer-controlled location, and the proxy itself is a natural place to add rate-limiting + spam mitigation later (item 2). GitHub App is the more architecturally pure answer but the setup tax doesn't pay off until volume is high.

**Hard requirement regardless of mechanism:** **explicit one-time consent.** First time a consumer's audit would file an issue, the skill MUST prompt: *"This audit found N actionable findings. File them as issues on `darylmcd/Roslyn-Backed-MCP`? Issues will include your finding details and audited workspace metadata (project count, server version). [Y/n/dry-run] (cached for future runs)."* Cached preference goes in plugin-local config (e.g. `~/.claude/.mcp-server-stress.json`). No silent "first run files 20 issues against a stranger's repo" surprise.

### (2) Spam risk — kick the can down the road

Quote: *"spam risk - this is a legitimate concern but really only affects us and our ability to 'filter the noise' and its honestly a) somewhat mitigatable and b) a can that can be kicked down the road a bit until it becomes an issue."*

**Implication:** don't pre-engineer mitigations for spam volume that doesn't exist yet. The brainstorm should land minimal-viable mitigation (issue template with required fields, `mcp-server-stress` label, rate limit per audit run) and explicitly defer the rest:

- **Land in v1:** required template, label, per-run rate limit (cap N issues per audit; rollup beyond that), required `audit-id` reference.
- **Defer until observed need:** confidence-threshold filtering, severity gating, deduplication-before-file, automated triage labels, abuse-detection at the proxy.

The proxy approach in (1) makes deferred mitigations easy to add server-side without re-shipping the skill.

### (3) Backward compatibility — keep fragments for maintainer use

Quote: *"backward compatibility - i like your lean on this. the github issue pattern is great for crowdsourcing issues but honestly overkill for us (its quicker to keep the fragments pattern local for us to consume)"*

**Implication: the fragment pattern stays for maintainer use indefinitely**, not just as a transition fallback. The maintainer's own multi-repo audit gets findings into `<sibling-repo>/backlog.d/` and `/backlog-intake` consolidates locally — fast, no auth, no proxy round-trip, no GitHub API hop. The issue-filing path is the consumer flow, not a replacement for the maintainer flow.

This changes the cost estimate (#3 from above is no longer "reshape `/backlog-intake`"; it's "extend `/backlog-intake` with an issues-input mode alongside the existing fragments-input mode") and changes the sunset story (no sunset for fragments; both flows coexist by design).

Concrete impact on backlog row sizing:
- `--target-output` flag now selects per-run: `fragments` (maintainer default), `issues` (consumer default), `both` (rare maintainer case).
- `/backlog-intake` grows an `--input=fragments|issues|both` flag, defaulting to `both`. Walks both sources.
- `eng/stage-review-inbox.ps1` keeps the fragment-discovery branch permanently. No deprecation.

This is materially less work than full sunset.

### (4) Issue triage discipline — agreed

User confirmed the proposed triage contract (every audit-filed issue labeled, weekly maintainer review SLA, accept/reject/defer states, "Closes #N" PR trailer to auto-close). Land as proposed.

---

## Naming convention (cross-cutting decision)

Quote: *"I dont like the 'audit-deep' naming. I think we should keep the naming as consistent as posisble around mcp-server-stress or mcp-server-stress-test as possible. i think that naming CLEARLY identifies the purpose of the skill where 'audit-deep' does not. to me audit-deep is valuable but it infers actual writes to a worktree/branch that will be actioned on (not disposable)"*

**Decision:** `mcp-server-stress` is the canonical name across all forward-looking surfaces. `audit-deep` only survives as historical references in:

- Closed backlog row ids (`audit-deep-collapse-modes`, `audit-deep-relocate-and-rename`) — history, don't rewrite.
- `changelog.d/audit-deep-relocate-and-rename.md` — fragment file already on disk, name preserved for fidelity.
- CHANGELOG entries from the 20260507 sweep — written in past tense, references stay.

**Forward-looking surfaces use `mcp-server-stress` consistently:**

- The skill location itself (already at `skills/mcp-server-stress/` once we reverse the relocate, currently at `.claude/skills/mcp-server-stress/`).
- Issue labels (`mcp-server-stress-finding`, NOT `audit-deep`). The label-rename in item (4)'s contract above and in the proposed issue-template scheme (#2 in *Open questions*) already used `audit-deep` — **mentally substitute `mcp-server-stress-finding` everywhere `audit-deep` appears as a label name in this doc**. The brainstorm should reflect that consistently in the row drafts.
- Issue templates (`.github/ISSUE_TEMPLATE/mcp-server-stress-finding.yml`).
- Proxy endpoint (if used): `mcp-server-stress.roslynmcp.dev/file-finding` or similar.
- Triage doc filename (`docs/MCP_SERVER_STRESS_FINDINGS.md`, NOT `AUDIT_FINDINGS.md`).

**Why this matters past aesthetics:** "audit-deep" semantically implies a deep audit that produces actionable persistent changes — which is exactly what we deliberately moved AWAY from with the disposable-worktree Phase 6 (audit run = no persistent change to audited repo). The new name signals "we exercise the surface and produce findings, but we don't *do* anything to your code." That's the user-facing promise we want consumers to internalize before granting auth or installing the App.

`mcp-server-stress` vs `mcp-server-stress-test`: pick one and stick with it. Current shipped path is `mcp-server-stress`. Stay with that — adding `-test` is more accurate but also more verbose, and consumers typing the slash-command will appreciate the shorter form. Defer the `-test` rename unless it becomes a clarity issue in practice.

---

## When this gets picked up

Next backlog sweep planning pass, or whenever you want to schedule the brainstorm. The pivot doesn't need to happen this week — the just-shipped sweep is functionally complete and the maintainer-only audit works. The pivot is about *widening* the producer set, not about fixing a broken thing. So: after a release cycle or two, when v1.35.0 has been out long enough to gauge whether community audits would actually happen.

If you decide *not* to pivot — that's fine, the just-shipped pattern is internally consistent. The cost would be that `/mcp-server-stress` stays a maintainer tool forever and the long-tail bugs that real-world consumer workspaces would surface stay invisible. Acceptable trade-off if community engagement is low.

---

## Dissent register and final decisions (2026-05-08, second turn)

After the user feedback section above was written, the user explicitly invited dissent on the resolutions. Three pushbacks landed and changed the design. **These supersede the earlier-section leans where they conflict.** Recording both the dissent and the final decision so the brainstorm starts from the resolved position, not the pre-pushback state.

### D1 — Single path, not two paths (supersedes "User feedback (3)")

**Original lean (above):** keep `backlog.d/` fragment pattern indefinitely for maintainer use; issues path runs alongside for consumers.

**Dissent:** two parallel paths is tech debt. Costs include schema drift across 4 surfaces (2 producers, 2 consumers), duplicate test coverage, and the implicit signal that maintainer findings are architecturally different — which they aren't, they're just consumed differently. The "fragments are quicker locally" argument is workflow preference, not architectural reason. A single-path design with a *label* encoding the trust difference is cleaner.

**Final decision:** **single path through GitHub Issues, with a `triaged-by-maintainer` label fast-path.**

- Maintainer-filed issues land with `triaged-by-maintainer` set at creation time (the skill detects "this is the maintainer's run" via repo-local config or a `--maintainer` flag and applies the label automatically).
- `/backlog-intake` honors the label: `triaged-by-maintainer`-labeled issues skip the human-triage step and convert directly to backlog rows.
- All other issues go through the normal triage workflow.
- `backlog.d/` fragment pattern + `eng/stage-review-inbox.ps1`'s fragment-discovery branch + `/backlog-intake`'s fragment walk **all retire** at v1 of this pivot. No "stays as fallback indefinitely" branch.

Cost-estimate update — **drop item #6** ("sunset and cleanup" of fragments) since fragments retire as part of the v1 pivot rather than later. **Update item #3** ("`/backlog-intake` reshape") from "extend with dual input" back to "replace fragment-walk with issue-list + maintainer-label fast-path" — same scope as the original sketch above.

### D2 — `print` mode IS v1, validate demand before infrastructure (supersedes "User feedback (1)")

**Original lean (above):** server-side proxy is the right starting answer; consumer zero-friction filing.

**Dissent:** friction also filters for engagement. The consumer willing to copy-paste a pre-formatted issue is the consumer who'll respond to triage questions and provide repro follow-up. Zero-friction automated filing produces low-context findings from people who don't even remember filing. Hosting infrastructure (Cloudflare Worker / Azure Function + secret rotation + abuse handling) is 1-2 weeks of work. Doing that *before* validating that consumers want to file findings at all is exactly the kind of premature infrastructure investment that bites later.

**Final decision:** **`print` mode IS v1.** Skill outputs ready-to-paste issue body for each finding; user files manually if they care. After 3 months of v1 usage, count filed issues. If volume warrants infrastructure, build the proxy or GitHub App then with concrete signal, not speculation. If volume is low, the lower-friction path was never the bottleneck — engagement was.

Cost-estimate update — **simplify item #2** ("GitHub Issues output mode") to ship `print` only. The `--issue-mode=create|preview|print` three-mode contract collapses to "skill prints issue bodies; if `gh` is authenticated AND user passes `--auto-file`, file via `gh issue create`." That's an opt-in fast path for the maintainer who already has `gh`, not a default for community runs.

### D3 — `mcp-server-surface-test` is the right name target (supersedes "Naming convention")

**Original lean (above):** `mcp-server-stress` is canonical; defer any further rename.

**Dissent:** "stress" semantically implies *load* testing — pummel with high concurrent traffic. That's not what the skill does. It does comprehensive surface exercise + apply-tool integration check + scorecard generation. "Stress" misleads consumers about what the skill will actually do (e.g. "this will pin my CPU" or "this will bombard my MCP with concurrent requests"). User confirmed the dissent and chose `mcp-server-surface-test` as the better target.

**Final decision:** **`mcp-server-surface-test` is the canonical name target.**

- The pivot's first row (or a separate prep row scheduled before it) renames `.claude/skills/mcp-server-stress/` → `skills/mcp-server-surface-test/` (also reverses the maintainer-only relocate from inits 2-3 of the recent sweep).
- All forward-looking surfaces use `mcp-server-surface-test`: the skill location, issue label (`mcp-server-surface-test-finding`), issue template (`.github/ISSUE_TEMPLATE/mcp-server-surface-test-finding.yml`), maintainer triage doc filename (`docs/MCP_SERVER_SURFACE_TEST_FINDINGS.md`), and any helper script names (`eng/aggregate-promotion-scorecards.ps1` is fine as-is — name doesn't reference the skill).
- Historical references stay: backlog row ids (`audit-deep-collapse-modes`, etc.), already-shipped CHANGELOG entries, the `audit-deep-relocate-and-rename.md` fragment file. Don't rewrite history.
- `audit-deep` is **not** a fallback or alias — gone forward. User explicitly stated: *"we arent using 'audit-deep'... i stand firm on that for now."*

**Cost note:** this is the third rename for this skill in its lifetime (`audit-deep` → `mcp-server-stress` → `mcp-server-surface-test`). Worth being honest in the eventual CHANGELOG entry: *"Renamed `mcp-server-stress` to `mcp-server-surface-test` (cumulative second public rename) — 'stress' implied load testing, which the skill does not do; 'surface-test' accurately describes the comprehensive-surface-exercise + apply-tool integration shape."* Pre-empt the "why does this keep changing names" friction with a clear rationale.

### D4 — Spam mitigation: minimum-viable-mitigation, not zero (clarification, not dissent — restating user's intent)

User clarified: *"i never meant zero mitigation just that minimal in v1 was sufficient until we were actually noticing the spam."* The doc's existing "spam risk" section already aligns with this — landing minimum-viable-mitigation in v1 (template required fields, label scheme, audit-id reference) and deferring enforcement (rate limits, abuse detection, dedup-before-file). No design change needed; this entry exists only to make the alignment explicit.

The schema lock-in piece is the part that's NOT optional in v1 — the issue-body template's required fields need to be designed once and held. Adding fields later when consumers are running old skill versions causes broken issues. Lock the schema day one even though enforcement is deferred.

---

## Net cost estimate — REVISED after dissent register

Replaces the earlier "Net cost estimate (rough — for brainstorm sizing)" section above. Same structure, applied resolutions:

1. **Restore shipped skill location + rename to `mcp-server-surface-test`** — combines the relocate-reversal with the second rename. `git mv .claude/skills/mcp-server-stress/ skills/mcp-server-surface-test/`, update test paths, update external references in `publish-preflight/SKILL.md`, `audit-phase-runner.md`, and any doc references. ≤4 prod files + 3 tests. **Combined naming-and-relocation row** so we don't churn surfaces twice.
2. **GitHub Issues output mode (print-only v1)** — Phase 19 of the prompt rewrites to print issue bodies (not file them). Optional `--auto-file` for `gh`-authenticated maintainer use. Issue-body template repurposed from `ai_docs/items/backlog-d-fragment-schema.md`. 2-3 prod files + 1 test (smaller than original estimate since we dropped the proxy + create-mode + preview-mode scope).
3. **`/backlog-intake` reshape (single-path)** — replace fragment-walk with `gh issue list --label mcp-server-surface-test-finding` + `triaged-by-maintainer` fast-path + standard triage flow. Retire the fragment-walk branch in this same row. 2-3 prod files + 1 test.
4. **Issue templates + label scheme + maintainer triage doc** — `.github/ISSUE_TEMPLATE/mcp-server-surface-test-finding.yml`, label definitions (`gh label create` script), triage doc at `docs/MCP_SERVER_SURFACE_TEST_FINDINGS.md`. ≤4 prod files, 0 tests.
5. **`/publish-preflight` Step 8 + scorecard aggregator extension** — optional issue-comment input for community-quorum scorecards. Stretch — split as a separate row, ship after #1-4 stabilize.

**Total: 4 v1 rows + 1 stretch.** Down from the original 6-row estimate, primarily because the fragment-pattern sunset row goes away (single-path means it retires inline with #3) and the issue-output mode is simpler (print-only).

The fragment-pattern retirement happens *as part of* row #3, not as a separate sunset row. Be honest in row #3's CHANGELOG entry: *"Retires the `<audited-repo>/backlog.d/` fragment pattern shipped in v1.34.x's `backlog-d-fragment-pattern` initiative. Single-path issue model replaces it; maintainer fast-path uses `triaged-by-maintainer` label instead of local-fragment shortcut."*

---

## Decisions still open for the brainstorm

After the dissent register, what remains genuinely undecided:

1. **Issue template required-field set.** Schema lock-in is critical (D4). What fields does the template require? Probably: `audit-run-id`, `server-version`, `audited-workspace-stats` (project count, LOC), `severity`, `area`, `anchors`, `repro-steps`, `proposed-fix`. Brainstorm should land the minimal set and the rationale.
2. **Triage SLA cadence and labels.** "Weekly maintainer review" was proposed but not confirmed. Labels: `mcp-server-surface-test-finding` (filed by audit), `triaged-by-maintainer` (auto-fast-path), `triaged:accepted` / `triaged:rejected` / `triaged:defer` (post-triage state). Confirm.
3. **Maintainer detection.** How does the skill know "this is the maintainer's run" to apply `triaged-by-maintainer`? Options: a `--maintainer` flag (explicit), reading a config file at `~/.config/roslyn-mcp/maintainer-mode` (configured once), checking if the audited repo IS Roslyn-Backed-MCP itself (heuristic), or just trusting `gh auth status` + repo-write permission as the gate. Brainstorm should pick.
4. **Pivot timing.** Ship after which release? v1.35.0 (the imminent release after the recent sweep) is too early — let it stabilize. Probably v1.36.x or v1.37.x. Confirm in the brainstorm.

The earlier "Open design questions" section above (numbered 1-7) is partially resolved by the dissent register. What survives unresolved: items 4 (triage discipline — confirmed but specifics open), 5 (cross-forge consumers), 6 (community-quorum scorecards — stretch), 7 (rate-limits — defer per D4).

---

## Drafted answers to open questions (2026-05-08, third turn)

The four questions above were drafted with explicit recommendations and brainstorm pressure points; the user reviewed and confirmed all four with no pushbacks. Recording here as **decisions, not drafts** — the brainstorm's job is to sanity-check, not re-design. Pressure points are kept as input to that sanity check.

### Q1 — Issue template required-field set

**Tradeoff frame:** every required field is friction at file-time and information at triage-time. Too few → "please add X" comment for every issue. Too many → consumers abandon the file step.

**Required fields (8):**

| Field | Why required | Format |
|---|---|---|
| `audit-run-id` | Ties N findings from one audit run together; lets the maintainer see the full audit context (prose report) for any issue without searching. Mandatory because issues filed in isolation lose all context within a week. | Kebab-slug `<repo-slug>-<utc-ts>`, generated by the audit at run start, included in every issue from that run. |
| `finding-id-within-run` | Granularity inside an audit run for cross-referencing related findings ("see also #1234, same audit run"). Trivial to generate; pays off at triage. | Integer 1..N within an `audit-run-id`. |
| `server-version` | Half of "is this still a bug?" questions are answered by "what version were you on?" Answering at file-time eliminates a triage round-trip. | Plain string from `mcp__roslyn__server_info`. Example: `1.34.2`. |
| `severity` | Drives triage priority. P0/P1 trigger fast-path; P2/P3 batch. Consumer-side gating depends on this. | Enum `P0`/`P1`/`P2`/`P3`. Audit assigns based on the prompt's existing severity rubric — not consumer judgment. |
| `area` | Triage routing. Even with one maintainer, area enables search-by-area. | Enum `tools`/`resources`/`prompts`/`skills`/`concurrency`/`perf`/`docs`/`other`. |
| `audited-workspace-shape` | Reproduces the half of bugs that are scale-dependent. "Worked on 5-project sample, broke on 200-project monorepo" is invisible without this. | Structured: `{ projectCount, totalLOC, languages, hasGenerators, targetFrameworks }`. Audit captures from `workspace_load`. |
| `finding-summary` | Fast-skim title for the triage queue. Issue title templated from this. | One-line, ≤120 chars, sentence-case. |
| `repro-context` | Trace of what the audit was doing when this surfaced. Without it, half of audit findings are unreproducible. | Structured: phase number, tool call sequence, last-N relevant log lines. |

**Optional fields (3):**

- `proposed-fix` — the audit prompt may or may not have hypotheses; don't make consumers fabricate one.
- `anchors` — `file:line` list. Useful for tools-area findings, less for concurrency/perf. Required-by-area would be cleaner but adds template complexity; defer to v2 if triage starts asking.
- `reproducer` — minimal repro repo or snippet. Hard to require because audits usually find things in the audited workspace, which the consumer may not be able to share.

**Anti-patterns explicitly forbidden:**

- Free-text "describe the bug" without structured fields. Defeats schema discipline. Template MUST surface required-field validation.
- "Attach screenshots" — meaningless for stdio + text output.
- "Operating system" without "MCP server platform" qualifier — consumer's OS doesn't matter; the server's runtime matters (already captured via `server-version`).

**Schema-lock-in considerations (per D4):** these 8 required + 3 optional are the v1 contract. Adding a field later breaks consumers running old skill versions — so the v1 set must be high-confidence. Deferred-to-v2: client identifier (Cursor vs VS Code vs Claude Code), client-side LLM (sonnet vs gpt-4) — consumers may not even know.

**Brainstorm pressure points (input to sanity-check pass):**

- Is `audited-workspace-shape` really required, or "encouraged"? Lean: required. Reproducibility lives or dies on this. "You may want to fill this in" templates don't get filled in.
- Should `anchors` be required-by-area (mandatory for `tools`/`prompts`, optional for `concurrency`/`perf`)? Cleaner contract, harder template. Lean: keep optional uniformly in v1; promote in v2 if triage demand grows.

### Q2 — Triage SLA cadence and labels

**Cadence:** weekly review, batched. Daily is overkill at expected volume; monthly creates a daunting backlog. Weekly batches enough volume per session for efficient processing while keeping issues responsive.

Specifically: maintainer triages all `mcp-server-surface-test-finding`-labeled issues filed in the prior 7 days, on a weekly cadence (day flexible). The SLA is "no untriaged issue older than 7 days," not "must triage Monday." P0 issues escalate out-of-band via the `urgent` label.

**Label scheme (12 labels):**

| Label | Applied by | Meaning |
|---|---|---|
| `mcp-server-surface-test-finding` | Audit at file-time | Filed by an audit run. Filter for triage queue. |
| `triaged-by-maintainer` | Audit at file-time, conditional on maintainer-mode (Q3) | Skip human triage; auto-fast-path through `/backlog-intake`. |
| `severity:P0`/`severity:P1`/`severity:P2`/`severity:P3` | Audit at file-time | From the required `severity` field. |
| `area:tools`/`area:resources`/`area:prompts`/`area:skills`/`area:concurrency`/`area:perf`/`area:docs`/`area:other` | Audit at file-time | From the required `area` field. |
| `urgent` | Maintainer or consumer | Out-of-band P0 escalation; bypasses weekly cadence. |
| `triaged:accepted` | Maintainer at triage | Will become a backlog row. Issue stays open until row ships. |
| `triaged:rejected` | Maintainer at triage | Not a real bug or out-of-scope. Issue closes with rationale. |
| `triaged:defer` | Maintainer at triage | Plausible but lacks evidence/repro/priority. Stays open with `defer-reason:<short>` comment. Quarterly re-evaluation. |
| `closes-row:<row-id>` | Maintainer at backlog-row creation | Cross-references the backlog row. Lets `/backlog-intake` re-find the issue when the PR ships. |

**Triage workflow:**

1. Maintainer runs `/backlog-intake` weekly.
2. Skill lists `mcp-server-surface-test-finding` issues opened in the prior 7 days, grouped by `area`, sorted by `severity`.
3. For each issue, maintainer presses `accept`/`reject`/`defer`. Skill applies labels.
4. For `accept`: skill drafts the backlog row, asks confirmation, applies `triaged:accepted` + `closes-row:<row-id>`, appends row to `ai_docs/backlog.md`. Issue stays open.
5. When the row ships, the closing PR uses `Closes #<issue>` trailer. GitHub auto-closes.
6. Quarterly: `/backlog-intake --review-deferred` walks `triaged:defer`-labeled issues. Most re-rejected (no new evidence); some promoted.

**Brainstorm pressure points:**

- Are 12 labels too many? Probably fine — GitHub label fatigue starts ~30+. We're at 11–13.
- Hard expiration on `triaged:defer` (auto-close at quarter X)? Lean: no. Hard expiration produces auto-close-then-reopen churn. Quarterly review is enough.

### Q3 — Maintainer detection

**Decision: `gh auth status` + repo-admin-permission heuristic, with config-file override.**

**Mechanism:**

1. At audit start, if `--auto-file` is in play, the skill calls `gh api repos/darylmcd/Roslyn-Backed-MCP --jq .permissions.admin`. If `true`, this is a maintainer run; issues file with `triaged-by-maintainer`.
2. Result is cached in `~/.config/roslyn-mcp/maintainer-mode-cache.json` for 24h to avoid hammering the API across multiple audit runs.
3. Override paths:
   - `--no-maintainer-fast-path` — explicit user override (e.g. maintainer wants to test the normal triage flow). Suppresses the label even if `gh` would grant it.
   - `~/.config/roslyn-mcp/maintainer-mode.json` with `{ forceMaintainer: true }` — explicit opt-in for users who don't have admin permissions but ARE pre-trusted (deputy maintainers, frequent contributors). Auto-applies the label.

**Edge cases:**

- `gh` not installed / not authenticated → no fast-path. Issues file normally with full triage queue. This is what we want for cold-onboard consumers.
- Maintainer's `gh` token expires → audit silently downgrades to normal triage. Maintainer notices at the next weekly review when their issue isn't pre-labeled.
- `gh` API call fails (rate-limited, network) → use 24h cache; if cache empty, default to normal triage. Don't block the audit on `gh` flakiness.

**Why this over the other options:**

- **`--maintainer` flag (explicit):** zero false-positives but annoying to type 50 times/year.
- **Config file only:** set-and-forget but copies across machines, leading to false-positives on un-authenticated machines.
- **Heuristic "audited repo IS Roslyn-Backed-MCP":** wrong — maintainer audits siblings frequently, those wouldn't be flagged.
- **`gh-permission` (chosen):** zero config; correct by default; degrades gracefully; one negligible API call per audit (compared to the 30+ minute audit duration).

Co-maintainers added later get fast-pathed automatically. That's the right behavior.

**Brainstorm pressure points:**

- Does this leak signal? `triaged-by-maintainer` on a public issue tells observers the audit was filed by a maintainer. Probably not sensitive — all the audit's evidence is already public on the issue. Worth confirming.

### Q4 — Pivot timing

**Decision: pivot scheduled for v1.36.x or later. v1.35.0 stabilizes first.**

- **v1.35.0** (imminent) — consumes the 7 fragments accumulated since v1.34.2. BREAKING due to the mode-collapse fragment, so minor bump under semver. Ships the 20260507 sweep + release-cut Step 6 fix + `--target=` contract. **No pivot work.**
- **Pivot row #1 (rename `mcp-server-stress` → `mcp-server-surface-test` + relocate back to shipped surface)** ships separately, possibly as v1.35.x patch or held for v1.36 — decoupled from the issues-pivot rows for cleaner review. Lean: separate row, separate release.
- **Pivot rows #2-#5 (issues-output mode, /backlog-intake reshape, templates+labels+triage doc, scorecard aggregator extension)** ship in v1.36.x or later. **Earliest 4–6 weeks after v1.35.0** to give:
  - Time for v1.35.0 to surface its own bugs (the audit-deep redesign was big).
  - Time for community to *try* v1.35.0 — nobody files audit findings against a version that just dropped.
  - Calendar margin so the pivot work isn't compressed.

**Why v1.36 minor and not v2.0:** semver minor with BREAKING fragments is fine; v2.0 implies a larger break than this is.

**Brainstorm pressure points:**

- Should rename ship decoupled from issues pivot? Lean: yes, separate row in v1.35.x or v1.36.0. Smaller PR, easier review, isolates the third-rename-fatigue concern.
- Brainstorm itself doesn't have a deadline. Park until v1.35.0 has been out 2–3 weeks, then run the brainstorm against this doc.

---

## Status after this turn

These decisions, the dissent register (D1–D4), and the original draft sections are all the brainstorm's input. **The brainstorm's job is sanity check, not redesign** — frame the prompt explicitly as "pressure-test these decisions, look for gaps, propose alternatives if you see them, DO NOT just confirm what's there." The brainstorm pressure points listed under each question are the seed prompts.

**Net cost estimate stands at 4 v1 rows + 1 stretch + 1 prep (the rename/relocate as separate row).** Roughly 5–6 rows total, depending on whether the rename rolls into the relocate or ships separately.
