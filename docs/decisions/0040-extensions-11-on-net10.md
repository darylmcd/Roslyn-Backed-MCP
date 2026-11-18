# ADR 0040 — Move the Microsoft.Extensions runtime family to 11.x on `net10.0`

- **Status:** Accepted (2026-11-18)
- **Deciders:** repository maintainers
- **Scope:** `Microsoft.Extensions.Hosting`, `.Http`, `.Logging`, `.Logging.Console` central pins
- **Supersedes:** the "stays on the 10.0.x servicing line" clause of ADR 0039
- **Superseded by:** nothing

## Context

ADR 0039 raised the source-build SDK floor to `11.0.100` and left the Extensions runtime family on
10.0.x. The maintainers decided to move that family to the 11.x line in the same wave. The 11.0.0
packages ship `net10.0` assets, so the target framework and the runtime prerequisite of the published
package do not change.

## Decision

Pin the four Extensions runtime family packages to 11.0.0 together. Keep the target framework at
`net10.0`.

## Compatibility

| Area | Effect |
|---|---|
| Stable tool, resource, and prompt contracts | None. This is a minor-version change under the release policy. |
| Runtime prerequisite | None. Installed users still need only .NET 10. |
| Shipped assembly closure | 11.x copies of libraries the .NET 10 shared framework also provides (for example `System.Text.Json`, pulled by `Microsoft.Extensions.Logging.Console`) now ship beside the server and win over the framework copies. The server loads the user's SDK MSBuild in the same process, so workspace load, `build_workspace`, and `test_run` are the paths to watch. |
| Servicing | .NET 11 is a standard-term release; the 11.x packages leave support before the .NET 10 long-term line does. The family must move again, or the target framework must move, before then. |
| Operational output | Host startup, shutdown, and console log formatting come from the 11.x libraries. That output goes to stderr and is not part of the MCP wire contract. |

## Consequences

- Dependabot services the family on the 11.x line.
- A regression traced to the app-local 11.x `System.*` libraries is resolved by returning the family
  to 10.0.x, which this ADR's supersession would record.
