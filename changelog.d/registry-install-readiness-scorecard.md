---
category: Added
---

- **Added:** `eng/verify-registry-readiness.ps1` — first slice of the MCP registry install-readiness scorecard (BRAIN-002). Validates `.claude-plugin/server.json` against MCP registry expectations (name format, `$schema` URL, packages[] shape, transport, identifier match to NuGet PackageId, version match to `Directory.Build.props`), cross-checks `plugin.json` and `marketplace.json` round-trip, and emits a structured artifact at `artifacts/registry-readiness.json` (`schemaVersion: 1`) that `/publish-preflight` Step 8.7 reads to surface a pass/fail/warn verdict. Wired into `verify-release.ps1` as a non-blocking quiet step and exposed via `just verify-registry-readiness`. Closes `registry-install-readiness-scorecard`.
