# ADR 0013: Default auto-restore of never-restored projects on workspace load

Status: Accepted, 2026-10-01 (additive 4.x change; no deprecation window required).

## Context

`workspace_load` / `workspace_reload` declared `autoRestore` as a plain `bool` defaulting to
`false`, so an omitted value was indistinguishable from an explicit `false`. A freshly cloned
checkout (no `project.assets.json`) therefore loaded with `restoreRequired: true` and every
semantic tool degraded until the caller discovered `nextCall` and reissued the load. A default
restore could not simply be turned on: any restore failure threw, so a load that previously
succeeded would start failing.

The staleness probe also could not tell a never-restored project from package-version drift, and
it read only `<projectDir>/obj/project.assets.json`, so `UseArtifactsOutput` and custom
`MSBuildProjectExtensionsPath` layouts looked "never restored".

## Decision

`autoRestore` is nullable and tri-state:

| Value | Behavior |
|---|---|
| omitted | When `restoreRequired` is true AND a project that declares `PackageReference` items has no `project.assets.json`, run `dotnet restore` and reload once. A failed or timed-out restore does not fail the load: `restoreRequired` stays `true`, `nextCall` is kept, and the payload adds a path-free `restoreFailureReason`. Package-version drift alone does not restore. |
| `true` | Unchanged: restore for any `restoreRequired` (drift included) and fail the call when the restore fails. |
| `false` | Never restore. |

Caller cancellation always propagates. The restore remains bounded by `RestoreTimeout` clamped to
the request deadline (row `workspace-restore-budget`) and runs through the
per-workspace command gate.

The assets file is located where MSBuild writes it (`ProjectAssetsFile`, else
`MSBuildProjectExtensionsPath`) when it is absent from `obj/`, so relocated layouts are not
misread as missing. The default `obj/` layout stays a file-exists check with no MSBuild evaluation.

## Consequences

- Wire: the `autoRestore` schema type widens from `boolean` to nullable boolean; `restoreFailureReason`
  is an additive optional field. Clients must ignore unknown fields (`docs/release-policy.md`).
- Behavior: an omitted-argument load of a never-restored project now spawns `dotnet restore`
  (bounded) where it previously returned `restoreRequired: true` immediately. Callers wanting the
  old behavior pass `autoRestore: false`.
- `dotnet restore` mutates `obj/` and the NuGet cache on first load; air-gapped hosts without a
  reachable feed see `restoreFailureReason` instead of a failed load.
