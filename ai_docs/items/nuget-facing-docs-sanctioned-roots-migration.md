# nuget-facing-docs-sanctioned-roots-migration — the page an upgrader lands on omits the escape hatch

## Anchors

- `README.md`
- `docs/setup.md`

## Acceptance

- [ ] `README.md` "Option A — Install As A Global Tool" states that a filesystem boundary must be configured before use, with a copy-ready `env` snippet — the current section shows only the install command.
- [ ] `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` appears in the README configuration table with its default and its intended use as a temporary compatibility escape hatch for the empty-boundary case only.
- [ ] A short "Upgrading from 2.x" note (README or a linked doc) states what an existing consumer must do BEFORE upgrading, and that client `roots/list` no longer drives discovery — supply a file path, configure a single-solution root, or call `workspace_load` explicitly.
- [ ] The README Security section reflects the server-owned boundary model rather than only the generic "path validation is defense in depth" framing.

## Evidence

Verified at HEAD, and deliberately scoped AGAINST a full rewrite — the README is NOT broadly stale:

- It was last updated 2026-08-13 in the same PR that landed the breaking change, and already documents `ROSLYNMCP_SANCTIONED_ROOTS` in prose and in the config table, including the per-platform delimiter and the fail-closed behavior.
- Its surface counts are CI-gated by `ReadmeSurfaceCountTests`, so they cannot drift.
- All 17 of its relative links resolve.

The real gaps are narrow and specific:

1. `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` appears ZERO times in `README.md` (once in `docs/setup.md`, and in the thrown error string). It is the one escape hatch a broken upgrader needs, missing from the page they land on.
2. The `roots/list` deprecation appears only in `docs/decisions/0002-configured-sanctioned-root-boundary.md`. Nothing user-facing tells a client author that Roots-driven discovery stopped working.
3. No consumer migration guidance exists. `docs/upgrade-matrix.md` is maintainer-facing (toolchain/TFM/Roslyn pins), not a consumer upgrade path.
4. README "Option A" shows `dotnet tool install -g Darylmcd.RoslynMcp` with no env guidance, ~110 lines above where the required variable is documented.

## Context

Scope note: do NOT rewrite the README. The surface-count line is pinned by a regex in `ReadmeSurfaceCountTests`; churning it risks a red gate for no gain.
