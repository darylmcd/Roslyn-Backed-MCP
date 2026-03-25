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

## Release Checklist

1. Run `eng/verify-release.ps1`.
2. Review the generated publish hash manifest under `artifacts/manifests/`.
3. Review dependency audit output from CI.
4. Confirm `server_info` and `server_catalog` reflect the intended support tiers.
5. Publish the host executable built from `src/RoslynMcp.Host.Stdio`.
6. Confirm docs remain synchronized (`README.md`, `AGENTS.md`, and `docs/product-contract.md`) for any surface or tiering change.

## Agent Session Release Gate

Before declaring a session complete for release-impacting work:

- build and tests pass
- machine-readable catalog and written docs agree
- stable vs experimental placement is intentional and documented
- changed behavior is covered by tests or explicitly deferred with rationale

## CI Gate

CI must run on every pull request and on `main`:

- restore
- build
- test
- publish host executable
- upload publish artifacts and hash manifest
- run `dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive`
