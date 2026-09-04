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

- Stable tools, prompts, and resources keep backward-compatible request and response shapes within a release line.
- Experimental entries may change faster and can be promoted, renamed, or removed in a future minor release.
- Preview/apply token semantics are stable only within one running server instance and workspace version.
- Protocol-version-specific fields, methods, capabilities, and error codes must be selected from the
  negotiated or request-scoped protocol context. Product version is never a protocol-version proxy.
- Correcting a non-conforming stable wire shape or removing publicly observable sensitive detail is
  still a breaking change when a client could depend on the old behavior. Record the decision and
  migration, ship it in a major release, and cover every supported protocol era on the raw wire.
- Additive completeness/status fields are minor-version compatible only when existing fields retain
  their meaning and the advertised schema permits the addition. Consumers must ignore unknown fields.
- A security correction may omit the normal deprecation window when retaining the behavior would leak
  secrets or sensitive implementation detail; the ADR and migration note must explain the exception.

## Versioning And Deprecation

- Use semantic versioning for the published host artifact.
- Adding stable tools/resources/prompts is a minor-version change.
- Breaking a stable request/response contract is a major-version change.
- Experimental-surface changes must still be called out in release notes.
- Deprecate stable entries for at least one minor release before removal.

## Tool-Surface Consolidation

- [ADR 0009](decisions/0009-tool-surface-policy.md) defines formatting, text-edit,
  code-transform, file-lifecycle, and project-file risk buckets for preview/apply families.
- Consolidate apply routes only within one risk bucket; cross-bucket apply merges are prohibited.
- Keep deprecated aliases callable for at least one released minor version, and remove them only in
  the next major release with a migration note naming the canonical replacement.

## SDK And Protocol Migration Records

- [ADR 0003](decisions/0003-sdk-2x-wire-compatibility.md) records the direct
  `ModelContextProtocol` 1.4.1→2.1.0 adoption, evaluated intervening releases, dual-era wire
  decisions, breaking-correction migrations, and the distinction between SDK and RoslynMcp versions.
- [ADR 0006](decisions/0006-modelcontextprotocol-2-2-servicing.md) records the
  `ModelContextProtocol` 2.1.0→2.2.0 servicing review, confirms that the separate ASP.NET Core
  hybrid-session feature is outside this stdio product, and retains both supported protocol eras
  without a consumer migration.
- [ADR 0007](decisions/0007-tasks-extension-compatibility.md) adopts the exact
  `ModelContextProtocol.Extensions.Tasks` 2.2.0 / core 2.2.0 pair for later opt-in runtime delivery.
  It requires protocol 2026-07-28 plus per-request capability metadata, synchronous fallback, a
  host-owned allowlist, finite retention, process-lifetime handles, and secret-safe background
  failure handling. It does not promise that later extension and core releases remain in lockstep.
- Future SDK major upgrades must add or supersede an ADR before release. The record must include the
  exact package lineage, supported protocol eras, public-change classification, raw-wire evidence,
  and consumer migration for each breaking correction.

## Where To Bump The Version String

The repo has **seven** files that hold a literal version string. All seven must move together on every release. The drift check is automated (see *Pre-merge verification command* below); every bump PR must touch all seven or `verify-version-drift.ps1` will fail.

| # | File | Field | Consumer | Notes |
|---|------|-------|----------|-------|
| 1 | `Directory.Build.props` | `<Version>` element | MSBuild master | Flows to `AssemblyVersion`, `FileVersion`, and `InformationalVersion` (via SourceLink) for every project that inherits the props. This is what `server_info` returns at runtime and what the MCP `initialize` handshake reports as `serverInfo.version` — `Program.cs` reads it via reflection, so the host code itself is never edited for a version bump. |
| 2 | `.claude-plugin/plugin.json` | `"version"` | Claude Code plugin loader | Canonical for `/plugin update`. The Claude Code client uses this value to decide whether the cached install in `~/.claude/plugins/cache/.../<version>/` is stale. If you change code without bumping this, existing users will not see your changes (the install cache hash matches the recorded version). |
| 3 | `.claude-plugin/marketplace.json` | `plugins[].version` | Marketplace catalog entry | Discovery/listing. Per the [Claude Code plugins reference](https://code.claude.com/docs/en/plugins-reference#metadata-fields), if both `plugin.json` and the marketplace entry set `version`, `plugin.json` wins. Keep them aligned anyway — drift here surfaces in the `/plugin` discover UI. |
| 4 | `manifest.json` (repo root) | `"version"` | Legacy DXT-style manifest | Not read by Claude Code's plugin loader (which uses `.claude-plugin/plugin.json` per file #2). Kept for parity with other consumers / older tooling that still parses the DXT format. Bump it together with the others to avoid confusion. |
| 5 | `.claude-plugin/mcp.json` | `args[]` package token | Claude Code plugin launcher | Pins `dnx` to `Darylmcd.RoslynMcp@X.Y.Z`, keeping the plugin runtime aligned with its manifests. |
| 6 | `.claude-plugin/server.json` | Top-level `"version"` AND `packages[0].version` | MCP Registry manifest | The `io.github.darylmcd/roslyn-mcp` registry entry. Both fields must move together — the top-level `version` is the manifest version; `packages[0].version` is the published NuGet package version. The drift check enforces both. |
| 7 | `CHANGELOG.md` | `## [X.Y.Z] - YYYY-MM-DD` header | Release notes anchor | The new section header for the release line. Must be added (not edited) — historical entries stay in place. |

Locations that are **not** manual bumps (do not edit them):

- `src/RoslynMcp.Host.Stdio/Program.cs` — reads from the assembly attribute at runtime (`typeof(...).Assembly.GetName().Version`).
- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs` — reads `AssemblyInformationalVersionAttribute` at runtime for `server_info`.
- `eng/update-claude-plugin.ps1` — reads version from `~/.claude/plugins/installed_plugins.json` at runtime (the `1.6.0` in the doc-comment example is illustrative only).
- `eng/verify-release.ps1` — invokes `verify-version-drift.ps1`; it is the authoritative release gate, not another version source.

### Pre-merge verification command

Run this from the repo root before merging a version-bump PR:

```bash
grep -n -H -E '"version"|<Version>|^## \[[0-9]' \
    Directory.Build.props \
    .claude-plugin/plugin.json \
    .claude-plugin/marketplace.json \
    .claude-plugin/mcp.json \
    .claude-plugin/server.json \
    manifest.json \
    CHANGELOG.md
```

The grep is intentionally inclusive. When eyeballing the output:

- **Must agree on the new version:** `Directory.Build.props:<line>`, `.claude-plugin/plugin.json:3`, `.claude-plugin/marketplace.json` plugin entry (the `plugins[].version` line, **not** line 9), `.claude-plugin/mcp.json` package pin, `.claude-plugin/server.json` (both the top-level `version` AND `packages[0].version`), `manifest.json:4`, and the **top** `CHANGELOG.md` `## [X.Y.Z]` header.
- **Ignore:** `.claude-plugin/marketplace.json:9` is the marketplace catalog's own `metadata.version` schema version (`1.0.0`) — it does not track the plugin version. Older `## [X.Y.Z]` headers further down `CHANGELOG.md` are historical entries, not drift.

This drift check is automated: `eng/verify-version-drift.ps1` runs at the top of `eng/verify-release.ps1` and exits non-zero if any of the seven files disagree. The manual grep is still useful for quick eyeballing but is no longer the only gate.

## Release Checklist

1. Run `eng/verify-release.ps1`.
2. Review the generated publish hash manifest under `artifacts/manifests/`.
3. Review dependency audit output from CI.
4. Confirm `server_info` and `server_catalog` reflect the intended support tiers; for supported prior release pairs, use `roslyn://server/catalog-diff/{fromVersion}/{toVersion}` as a machine-readable review aid.
5. For material surface, preview/apply-behavior, or tiering changes, review the latest deep-review rollup under `ai_docs/reports/` and its raw evidence under `ai_docs/audit-reports/`.
6. **Run the version-string drift check** from [Where To Bump The Version String](#where-to-bump-the-version-string). All seven files must agree.
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

CI runs on pull requests, manual dispatch, and the weekly schedule. Push-to-`main` is intentionally
omitted because protected-branch changes arrive through a validated PR; see `CI_POLICY.md` for the
canonical trigger and runner contract.

- restore
- build
- test
- publish host executable
- upload publish artifacts and hash manifest
- run `pwsh -NoProfile -File ./eng/verify-nuget-audit.ps1` (fail-closed restore audit for top-level and transitive advisories)
