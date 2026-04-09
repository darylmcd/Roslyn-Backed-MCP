# Release Policy

For day-to-day agent execution flow, start from `AGENTS.md`. This document defines release gates and compatibility rules.

## Production-Ready Definition

A release is production-ready when all of the following are true:

- the stable contract in `docs/product-contract.md` matches `roslyn://server/catalog`
- `dotnet build RoslynMcp.slnx --nologo` succeeds
- `dotnet test RoslynMcp.slnx --nologo` succeeds
- `eng/verify-release.ps1` succeeds and writes publish hashes
- CI passes on the protected branch
- dependency audit output has been reviewed for vulnerable packages
- for material tool/resource/prompt surface or tiering changes, the latest deep-review rollup in `ai_docs/reports/` has been reviewed against its supporting raw audits in `ai_docs/audit-reports/`

## Compatibility Policy

- Stable tools/resources keep backward-compatible request and response shapes within a release line.
- Experimental entries may change faster and can be promoted, renamed, or removed in a future minor release.
- Preview/apply token semantics are stable only within one running server instance and workspace version.

## Versioning And Deprecation

- Use semantic versioning for the published host artifact.
- Adding stable tools/resources/prompts is a minor-version change.
- Breaking a stable request/response contract is a major-version change.
- Experimental-surface changes must still be called out in release notes.
- Deprecate stable entries for at least one minor release before removal.

## Where To Bump The Version String

The repo has **five** files that hold a literal version string. All five must move together on every release. There is currently no automated drift check (`eng/verify-release.ps1` does not validate them), so every bump PR must touch all five or it will ship inconsistent metadata.

| # | File | Field | Consumer | Notes |
|---|------|-------|----------|-------|
| 1 | `Directory.Build.props` | `<Version>` element | MSBuild master | Flows to `AssemblyVersion`, `FileVersion`, and `InformationalVersion` (via SourceLink) for every project that inherits the props. This is what `server_info` returns at runtime and what the MCP `initialize` handshake reports as `serverInfo.version` — `Program.cs` reads it via reflection, so the host code itself is never edited for a version bump. |
| 2 | `.claude-plugin/plugin.json` | `"version"` | Claude Code plugin loader | Canonical for `/plugin update`. The Claude Code client uses this value to decide whether the cached install in `~/.claude/plugins/cache/.../<version>/` is stale. If you change code without bumping this, existing users will not see your changes (the install cache hash matches the recorded version). |
| 3 | `.claude-plugin/marketplace.json` | `plugins[].version` | Marketplace catalog entry | Discovery/listing. Per the [Claude Code plugins reference](https://code.claude.com/docs/en/plugins-reference#metadata-fields), if both `plugin.json` and the marketplace entry set `version`, `plugin.json` wins. Keep them aligned anyway — drift here surfaces in the `/plugin` discover UI. |
| 4 | `manifest.json` (repo root) | `"version"` | Legacy DXT-style manifest | Not read by Claude Code's plugin loader (which uses `.claude-plugin/plugin.json` per file #2). Kept for parity with other consumers / older tooling that still parses the DXT format. Bump it together with the others to avoid confusion. |
| 5 | `CHANGELOG.md` | `## [X.Y.Z] - YYYY-MM-DD` header | Release notes anchor | The new section header for the release line. Must be added (not edited) — historical entries stay in place. |

Locations that are **not** manual bumps (do not edit them):

- `src/RoslynMcp.Host.Stdio/Program.cs` — reads from the assembly attribute at runtime (`typeof(...).Assembly.GetName().Version`).
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` — reads `AssemblyInformationalVersionAttribute` at runtime for `server_info`.
- `eng/update-claude-plugin.ps1` — reads version from `~/.claude/plugins/installed_plugins.json` at runtime (the `1.6.0` in the doc-comment example is illustrative only).
- `eng/verify-release.ps1` — does no version validation today.

### Pre-merge verification command

Run this from the repo root before merging a version-bump PR:

```bash
grep -n -H -E '"version"|<Version>|^## \[[0-9]' \
    Directory.Build.props \
    .claude-plugin/plugin.json \
    .claude-plugin/marketplace.json \
    manifest.json \
    CHANGELOG.md
```

The grep is intentionally inclusive. When eyeballing the output:

- **Must agree on the new version:** `Directory.Build.props:<line>`, `.claude-plugin/plugin.json:3`, `.claude-plugin/marketplace.json` plugin entry (the `plugins[].version` line, **not** line 9), `manifest.json:4`, and the **top** `CHANGELOG.md` `## [X.Y.Z]` header.
- **Ignore:** `.claude-plugin/marketplace.json:9` is the marketplace catalog's own `metadata.version` schema version (`1.0.0`) — it does not track the plugin version. Older `## [X.Y.Z]` headers further down `CHANGELOG.md` are historical entries, not drift.

This drift check is now automated: `eng/verify-version-drift.ps1` runs at the top of `eng/verify-release.ps1` and exits non-zero if any of the five files disagree. The manual grep is still useful for quick eyeballing but is no longer the only gate.

## Release Checklist

1. Run `eng/verify-release.ps1`.
2. Review the generated publish hash manifest under `artifacts/manifests/`.
3. Review dependency audit output from CI.
4. Confirm `server_info` and `server_catalog` reflect the intended support tiers.
5. For material surface, preview/apply-behavior, or tiering changes, review the latest deep-review rollup under `ai_docs/reports/` and its raw evidence under `ai_docs/audit-reports/`.
6. **Run the version-string drift check** from [Where To Bump The Version String](#where-to-bump-the-version-string). All five files must agree.
7. Publish the host executable built from `src/RoslynMcp.Host.Stdio`.
8. Confirm docs remain synchronized (`README.md`, `AGENTS.md`, and `docs/product-contract.md`) for any surface or tiering change.

## Agent Session Release Gate

Before declaring a session complete for release-impacting work:

- build and tests pass
- machine-readable catalog and written docs agree
- stable vs experimental placement is intentional and documented
- changed behavior is covered by tests or explicitly deferred with rationale
- for material surface or tiering work, audit evidence exists or the lack of a deep-review pass is explicitly deferred with rationale

## CI Gate

CI must run on every pull request and on `main`:

- restore
- build
- test
- publish host executable
- upload publish artifacts and hash manifest
- run `dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive`
