---
name: publish-preflight
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

This step consumes the promotion scorecard emitted by `/audit-deep mode=promotion-only`. It surfaces — but does not auto-apply — recommendations to promote experimental tools to stable in the upcoming release.

Read `ai_docs/audit-reports/_latest-promotion-scorecard.json`.

**Branch on file presence + freshness:**

| State | Decision | Output |
|---|---|---|
| File missing | INFO (not a fail) | "No promotion scorecard on file. Run `/audit-deep mode=promotion-only` if you want a promotion gate this release. Otherwise no action needed." |
| `generatedAt` ≤ 30 days old | PROCEED to inspection | Move to next bullet |
| `generatedAt` 30–90 days old | WARN | "Promotion scorecard is N days stale. Consider re-running `/audit-deep mode=promotion-only` before release; proceeding with stale data." |
| `generatedAt` > 90 days old | TREAT AS MISSING | "Promotion scorecard is older than 90 days; ignoring. Re-run `/audit-deep mode=promotion-only` if a promotion gate is desired this release." |
| File malformed / wrong `schemaVersion` | WARN | "Promotion scorecard exists but is unparseable (schemaVersion=N expected 1, or JSON parse error). Treating as absent." |

**When the scorecard is fresh:** filter `scorecard[]` to entries with `recommendation == "promote"`. For each:

1. Note the entry's `name`, `kind`, `category`, `currentTier`.
2. Locate the source-of-truth tier marker. For tools, this is the `[McpToolMetadata("category", "experimental", ...)]` attribute on the tool's method **plus** the matching entry in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.<Category>.cs`. (For resources, `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs`. For prompts, `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.*.cs`.)
3. Build a checklist for the maintainer:

   ```
   Promotion candidates from <generatedAt>:
   - <name> (<kind>, <category>) — currentTier=experimental, recommendation=promote, evidence=N items
       Edit: src/RoslynMcp.Host.Stdio/Tools/<file>.cs   — flip "experimental" → "stable" on the [McpToolMetadata] for <name>
       Edit: src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.<Category>.cs   — flip "experimental" → "stable" on the catalog entry for <name>
       Verify: dotnet test --filter SurfaceCatalogTests   — parity check passes after both edits
   ```

4. Ask the user explicitly: *"N tools recommended for promotion in this release. Apply now (manual edits, then re-run `/publish-preflight`), defer to a follow-up release, or skip the gate?"*

5. Whatever the user chooses, log the decision in the summary report. Promotion is **not** a precondition for publish — a maintainer can ship without acting on the scorecard. The gate's purpose is visibility, not enforcement.

**Pass** when the scorecard was either absent, treated-as-absent, or read cleanly. **Fail** is reserved for: scorecard present + fresh + malformed in a way that prevents reading recommendations (e.g. truncated JSON). Stale-but-readable is WARN, not FAIL.

**(Future automation — backlog `audit-deep-release-cut-promotion-gate`: a `/promote-tier <tool> stable` skill will replace the manual edits in step 3 with one tool call. Until that ships, the manual checklist is the contract.)**

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

Overall: READY TO PUBLISH / NOT READY (N issues)
```

If all pass, tell the user: "All checks passed. To publish: create a GitHub Release (which triggers the publish-nuget workflow) or run `eng/publish-nuget.ps1` manually."

If any fail, list the failures with remediation steps. Step 8's WARN/INFO never blocks publish — it only changes the wording of the overall summary line.
