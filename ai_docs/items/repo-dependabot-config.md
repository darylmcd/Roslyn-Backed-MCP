# repo-dependabot-config — add Dependabot (actions + nuget) and bump stale actions

**row:** `repo-dependabot-config` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/dependabot.yml` (new file)
- `.github/workflows/ci.yml:42` (`checkout@v4`), `:96` (`cache@v4`), `:142`/`:148`/`:156` (`upload-artifact@v4`)
- `.github/workflows/publish-nuget.yml:27` (`checkout@v4`), `:31` (`setup-dotnet@v5`), and the `publish-registry` job's `checkout@v4`

## Acceptance

- [ ] `.github/dependabot.yml` present with `github-actions` (weekly) and `nuget` ecosystems.
- [ ] `actions/checkout`, `actions/cache`, `actions/upload-artifact` bumped to the current major that targets Node 24 (clears the "Node.js 20 is deprecated … forced to run on Node.js 24" CI warning).

## Evidence

- 2026-06-19 CI review: no `dependabot.yml` / Renovate config found in the repo; the Node 20 deprecation warning appears on every CI run because the action versions are stale. Dependabot would auto-PR these going forward.

## Context

Low risk — config + version bumps only. The action bump and the Dependabot config are the same dependency-hygiene concern; ship together.
