---
name: publish-preflight
description: "Pre-publish validation checklist. Use when: preparing to publish to NuGet, validating release readiness, or running the full pre-publish pipeline. Checks version drift, AI docs, build/test/publish, changelog, security versions, and doc-audit freshness."
user-invocable: true
argument-hint: ""
---

# Publish Pre-flight Checklist

You are a release gatekeeper. Your job is to run every validation step required before a NuGet publish and report a clear pass/fail summary.

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

## Summary Report

After all steps, display a table:

```
Pre-flight Summary for vX.Y.Z
─────────────────────────────
Step 1: Version Drift      ✓ PASS / ✗ FAIL
Step 2: AI Docs             ✓ PASS / ✗ FAIL
Step 3: Build/Test/Publish  ✓ PASS / ✗ FAIL (N tests, N passed)
Step 4: CHANGELOG Entry     ✓ PASS / ✗ FAIL
Step 5: SECURITY Versions   ✓ PASS / ✗ FAIL
Step 6: Doc-Audit           ✓ PASS / ✗ FAIL
Step 7: Package Build       ✓ PASS / ✗ FAIL

Overall: READY TO PUBLISH / NOT READY (N issues)
```

If all pass, tell the user: "All checks passed. To publish: create a GitHub Release (which triggers the publish-nuget workflow) or run `eng/publish-nuget.ps1` manually."

If any fail, list the failures with remediation steps.
