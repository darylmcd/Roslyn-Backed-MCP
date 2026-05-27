---
name: publish-preflight
installed_as: publish-preflight
description: "Pre-publish validation checklist. Use when: preparing to publish to NuGet, validating release readiness, or running the full pre-publish pipeline. Checks version drift, AI docs, build/test/publish, changelog, security versions, and doc-audit freshness."
user-invocable: true
argument-hint: ""
---

# Publish Pre-flight Checklist

You are a release gatekeeper. Your job is to run every validation step required before a NuGet publish and report a clear pass/fail summary.

## Server discovery

Roslyn MCP **`server_info`** / **`server_catalog`** describe the *running analyzer server*, not this repo's release scripts. This skill is about **repository** publish gates.

## Repo shortcuts ([just](https://github.com/casey/just))

From the repository root you can run:

| Step | `just` recipe |
|------|----------------|
| Version drift | `just verify-version-drift` |
| AI docs | `just verify-docs` |
| Full release script (build, test, coverage, publish, manifest) | `just verify-release` |
| Pack host nupkg | `just pack` |
| Aggregate CI-like local run | `just ci` or `just full` |

These wrap the same `eng/*.ps1` scripts below when you prefer a single entry point.

## Checklist Steps

Execute ALL steps in order. Track pass/fail for each. Do NOT stop on the first failure — run the full checklist so the user sees everything that needs fixing.

### Step 1: Version Drift Check

Run via Bash:
```
pwsh -NoProfile -File eng/verify-version-drift.ps1
```

**Pass** if exit code 0. **Fail** if any of the 5 version files disagree — report which ones.

### Step 2: AI Documentation Validation

Run via Bash:
```
pwsh -NoProfile -File eng/verify-ai-docs.ps1
```

**Pass** if exit code 0. **Fail** if documentation structure is invalid.

### Step 3: Build, Test, and Publish Validation

Run via Bash:
```
pwsh -NoProfile -File eng/verify-release.ps1 -Configuration Release
```

This runs: version drift (again, harmless), restore, build, test with coverage, publish host binary, and SHA256 manifest generation.

**Pass** if exit code 0. **Fail** if build errors, test failures, or publish errors.

Extract and report:
- Total tests / passed / failed
- Coverage output path
- Hash manifest path

### Step 4: CHANGELOG.md Entry

Read `Directory.Build.props` to get the current version. Read `CHANGELOG.md` and check that a `## [X.Y.Z]` header exists for the current version.

**Pass** if the header exists. **Fail** if missing — remind the user to run `/roslyn-mcp:bump` or manually add the section.

### Step 5: SECURITY.md Supported Versions

Read `SECURITY.md` and extract the supported-versions table. Read the current version from `Directory.Build.props`. Check that the major.minor line (e.g., `1.8.x`) appears in the "Yes" row.

**Pass** if the current major.minor is listed as supported. **Fail** if the table is stale — report what it says vs what it should say.

### Step 6: Doc-Audit (Consumer README Freshness)

Invoke the `/doc-audit` skill to check that consumer-facing documentation is current. If the `/doc-audit` skill is not available, manually check:
- `src/RoslynMcp.Host.Stdio/README.md` exists and references the current version
- The tool count in the README roughly matches `server_info` stable + experimental counts

**Pass** if the consumer README is current. **Fail** with specific staleness notes.

### Step 7: Package Build Verification

Run via Bash:
```
dotnet pack src/RoslynMcp.Host.Stdio -c Release -o /tmp/preflight-nupkg --nologo
```

Check that both `.nupkg` and `.snupkg` are produced. Verify the `.nupkg` contains `icon.png` and `README.md`.

**Pass** if both packages exist with expected content. **Fail** with details.

### Step 8: Promotion Scorecard Gate (advisory, non-blocking)

This step consumes per-repo promotion scorecards emitted by `/mcp-server-stress` and aggregates them across all configured sibling repos. It surfaces — but does not auto-apply — recommendations to promote experimental tools to stable in the upcoming release.

**Aggregation contract.** Each audited repo writes its own `<audited-repo>/ai_docs/audit-reports/_latest-promotion-scorecard.json` (per-repo, alongside the prose audit report — the legacy `<Roslyn-MCP-root>/ai_docs/audit-reports/_latest-promotion-scorecard.json` last-write-wins file is deprecated). The aggregator script gathers every sibling's scorecard, merges them keyed by `<kind>|<name>`, and emits a quorum-aware verdict per entry:

- `promote: ready` — at least 2 sibling repos voted `promote` AND zero `keep-experimental` AND zero `deprecate` votes.
- `promote: blocked` — at least one `keep-experimental` or `deprecate` vote (a single workspace's hard-stop blocks the quorum).
- `needs-more-evidence` — fewer than 2 `promote` votes and no blockers.

Run the aggregator via Bash:
```
pwsh -NoProfile -File eng/aggregate-promotion-scorecards.ps1
```

(Optional: pass `-SiblingRepoParent` and `-ExcludeRepoFolders` to override the discovery defaults; see the script's comment-based help via `pwsh -NoProfile -File eng/aggregate-promotion-scorecards.ps1 -?`.)

The script emits a single JSON object on stdout. Parse it and branch on the result:

| State | Decision | Output |
|---|---|---|
| `summary.noScorecardsAvailable == true` (zero siblings have a scorecard on file) | INFO (not a fail) | "No promotion scorecard on file from any sibling repo. Run `/mcp-server-stress` in a sibling repo if you want a promotion gate this release. Otherwise no action needed." |
| Some siblings missing (`siblingReposMissingScorecard` non-empty AND `siblingReposWithScorecard` non-empty) | WARN, then PROCEED | "Promotion scorecards missing from these sibling repos: \<list\>. Aggregating across the \<N\> repos that do have scorecards." |
| Per-repo scorecard older than 30 days | WARN per repo | "Promotion scorecard from \<repo\> is N days stale. Consider re-running `/mcp-server-stress` in that repo before release." |
| Per-repo scorecard older than 90 days | TREAT AS MISSING for that repo | "Promotion scorecard from \<repo\> is older than 90 days; excluding from the quorum. Re-run `/mcp-server-stress` there if its evidence matters." |
| Aggregator JSON malformed / wrong `schemaVersion` | WARN | "Aggregator output is unparseable (schemaVersion=N expected 1, or JSON parse error). Treating as absent." |
| Legacy single-file scorecard found at `<Roslyn-MCP-root>/ai_docs/audit-reports/_latest-promotion-scorecard.json` | WARN (one-line) | "scorecard at deprecated path: `<Roslyn-MCP-root>/...`; expected per-repo path under `<audited-repo>/ai_docs/audit-reports/`. The aggregator ignores this file. Migrate or delete it manually." |

**When the aggregator returns entries:** filter `entries[]` by `verdict`:

1. **`verdict == "promote: ready"`** — promotion candidates. For each:
   - Note the entry's `name`, `kind`, `category`, `currentTier`, `promoteVotes`, and `sourceRepos.promote` list.
   - Build a checklist for the maintainer:

     ```
     Promotion candidates (quorum: ≥2 sibling repos with `promote`, 0 blockers):
     - <name> (<kind>, <category>) — currentTier=experimental, promoteVotes=N from <repo-list>
         Run: /promote-tier <name> stable
         Verify: dotnet test --filter SurfaceCatalogTests   — parity check passes after the flip
     ```

   - Ask the user explicitly: *"N tools recommended for promotion in this release. Apply now via `/promote-tier` (one per tool, then re-run `/publish-preflight`), defer to a follow-up release, or skip the gate?"*

2. **`verdict == "promote: blocked"`** — surface as WARN with the per-repo blocker reasons from the entry's `blockers` array. Do not treat blocked entries as promotion candidates.

3. **`verdict == "needs-more-evidence"`** — informational only; list briefly and recommend re-running `/mcp-server-stress` in additional sibling repos to broaden the sample size.

4. Whatever the user chooses, log the decision in the summary report. Promotion is **not** a precondition for publish — a maintainer can ship without acting on the scorecard. The gate's purpose is visibility, not enforcement.

**Pass** when the aggregator ran cleanly (regardless of whether any entries were `promote: ready`). **Fail** is reserved for: aggregator exit code non-zero, or output that prevents parsing. Stale-but-readable scorecards are WARN, not FAIL.

### Step 8.5: Experimental Age Audit (advisory, non-blocking)

This step surfaces experimental-tier surface entries that have been experimental for more than 180 days without promotion or deprecation. It does not auto-promote or auto-deprecate any entry; it is informational only.

Run the auditor via Bash:
```
pwsh -NoProfile -File eng/audit-experimental-age.ps1
```

(Optional: pass `-ThresholdDays <N>` to lower or raise the age threshold; see `pwsh -NoProfile -File eng/audit-experimental-age.ps1 -?` for all parameters.)

The script emits a Markdown table of experimental entries whose `git blame` age exceeds `$ThresholdDays` days (default 180). If no entries exceed the threshold, the script prints a one-line "no entries found" message and exits 0.

**Action:** Review the table. For each entry that has aged significantly with no promotion path in sight, consider opening a backlog row to either promote it (if real-world usage supports stable tier) or deprecate it (if it has not seen adoption). No action is required before publishing; the gate's purpose is visibility, not enforcement.

**Pass** always — the script always exits 0. Include the output (or a "no stale entries" note) in the summary report as an informational line.

### Step 8.7: Registry Install-Readiness Scorecard (advisory, non-blocking)

This step consumes the structured artifact emitted by `eng/verify-registry-readiness.ps1` (which `verify-release.ps1` produces in Step 3). The artifact lists every MCP-registry / plugin-manifest / marketplace-manifest cross-check, an overall verdict (`ready` or `not-ready`), and pass/fail/warn counts.

Read `artifacts/registry-readiness.json` and report:

- `summary.overall` (`ready` / `not-ready`)
- `summary.pass` / `summary.fail` / `summary.warn` counts
- Each entry in `checks[]` whose `status` is `fail` or `warn` — surface the `id` and `message`

**Pass** when `summary.fail == 0` (warn-only is still pass). **Fail** when `summary.fail > 0`; report each FAIL row with its `id` and `message` so the maintainer knows which field to fix. If the artifact is absent, run `pwsh -NoProfile -File eng/verify-registry-readiness.ps1` directly and re-read.

This gate is advisory — it does not block publish, but a `not-ready` verdict means the MCP registry submission would be rejected or the install instructions would surface drift to consumers.

## Summary Report

After all steps, display a table:

```
Pre-flight Summary for vX.Y.Z
─────────────────────────────
Step 1: Version Drift            ✓ PASS / ✗ FAIL
Step 2: AI Docs                   ✓ PASS / ✗ FAIL
Step 3: Build/Test/Publish        ✓ PASS / ✗ FAIL (N tests, N passed)
Step 4: CHANGELOG Entry           ✓ PASS / ✗ FAIL
Step 5: SECURITY Versions         ✓ PASS / ✗ FAIL
Step 6: Doc-Audit                 ✓ PASS / ✗ FAIL
Step 7: Package Build             ✓ PASS / ✗ FAIL
Step 8: Promotion Scorecard Gate  ✓ PASS / ⚠ WARN / ℹ INFO  (N candidates surfaced, M accepted, K deferred)
Step 8.7: Registry Readiness      ✓ PASS / ✗ FAIL (N pass / M fail / K warn)

Overall: READY TO PUBLISH / NOT READY (N issues)
```

If all pass, tell the user: "All checks passed. To publish: create a GitHub Release (which triggers the publish-nuget workflow) or run `eng/publish-nuget.ps1` manually."

If any fail, list the failures with remediation steps. Step 8's WARN/INFO never blocks publish — it only changes the wording of the overall summary line.
