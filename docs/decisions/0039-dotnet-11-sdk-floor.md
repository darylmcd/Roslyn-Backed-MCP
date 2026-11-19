# ADR 0039 — Raise the source-build SDK floor to 11.0.100 for Roslyn 5.12 and MSBuild 18.x

- **Status:** Accepted (2026-11-18), amended 2026-11-18
- **Deciders:** repository maintainers
- **Scope:** `global.json` SDK floor, the Roslyn API family, the `Microsoft.Build*` compile family, CI SDK lanes
- **Supersedes:** nothing
- **Superseded by:** nothing (ADR 0040 decides the Extensions family)

## Context

RoslynMcp analyzes user workspaces with the Roslyn it ships. Pinned to Roslyn 5.9, the server cannot
parse language features that arrive with the .NET 11 SDK compiler, so workspaces built with that SDK
would degrade as they adopt them.

Three constraints tie the Roslyn pin to the build toolchain:

| Constraint | Evidence |
|---|---|
| Each stable Roslyn version ships with one SDK band. | SDK `10.0.404` carries Roslyn 5.9.0 and MSBuild 18.9; SDK `11.0.100` carries Roslyn 5.12.0 and MSBuild 18.12. |
| The in-repo analyzer is built against the central Roslyn pin and loaded by the build of `RoslynMcp.Host.Stdio`. | An analyzer built against a newer Roslyn than the SDK compiler fails with `CS9057`; the same mechanism forced the earlier `10.0.100` to `10.0.400` correction. |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.12.0 depends on `Microsoft.Build.Framework` 18.7.1. | MSBuild 18.x brings `Microsoft.NET.StringTools` as a runtime dependency, and `Microsoft.Build.Locator` rejects it (`MSBL001`) unless it is excluded like the other MSBuild references. |

Dependabot proposed the Roslyn family at 5.12.0 on the `10.0.400` floor; every validation leg failed
with `MSBL001`.

## Decision

1. Raise the source-build SDK floor in `global.json` from `10.0.400` to `11.0.100`
   (`rollForward: latestFeature` unchanged).
2. Keep the target framework at `net10.0`. The published package and its runtime prerequisite do not
   change.
3. Move the seven Roslyn API family pins to 5.12.0 together.
4. Move the four `Microsoft.Build*` compile references to 18.7.1 and add `Microsoft.NET.StringTools`
   18.7.1 to the same family, centrally pinned and referenced with `ExcludeAssets="runtime"`
   and `PrivateAssets="all"`. 18.7.1 is the lowest version Roslyn 5.12 accepts; choosing the lowest
   keeps the compiled MSBuild API surface inside what older installed SDKs load at run time.
5. CI and publish jobs install the `11.0.x` SDK plus `10.0.x` for the `net10.0` runtime. The exact
   floor lane becomes `sdk-floor (11.0.100)`.
6. Remove the Dependabot ignore for `Microsoft.Build*` 18.x and route `Microsoft.NET.StringTools`
   with the MSBuild compile family.

**Amendment (2026-11-18).** Dependabot is told to ignore the five compile-family packages
entirely. Point 4 pins them at the lowest version the Roslyn API family accepts, so routine
servicing proposals contradict the decision; they are compile-time references and ship no
assemblies. The first proposal after this ADR also could not build: `Microsoft.Build` 18.10 and
later target `net11.0` only, so Dependabot split the family between 18.9.6 and 18.12.2. The
family moves by hand, in the same change as a Roslyn API family update that requires it.

The `Microsoft.Extensions.*` runtime family is not part of this decision; its move to 11.x is
recorded in [ADR 0040](0040-extensions-11-on-net10.md).

## Compatibility

| Audience | Effect |
|---|---|
| Users of the published package or plugin | None. The package still targets `net10.0` and needs only .NET 10. |
| Source builders and contributors | Must install the .NET 11 SDK (`11.0.100` or a later 11.0.x band) and keep the .NET 10 runtime. |
| Tool contracts | No stable request or response shape changes by design. Roslyn 5.12 may report diagnostics and syntax for newer language versions that 5.9 did not understand. |

This is a minor-version change under the release policy: it alters the source-build requirement, not
a stable tool, resource, or prompt contract. Moving the target framework would change the runtime
prerequisite for installed users and needs its own decision.

## Consequences

- Dependabot can propose Roslyn API family updates again; the MSBuild compile family is ignored
  (see the amendment). A Roslyn update beyond the compiler of the SDK floor still requires raising
  the floor in the same change, and moving the compile family when the new Roslyn requires it.
- The exact-floor lane now proves the build on `11.0.100`; SDK 10 can no longer build the repository.
- `THIRD-PARTY-NOTICES.md` and `docs/upgrade-matrix.md` list `Microsoft.NET.StringTools`.
