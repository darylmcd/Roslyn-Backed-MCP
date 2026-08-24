# ADR 0006 — ModelContextProtocol 2.2 servicing

- **Status:** Accepted (2026-08-24)
- **Deciders:** repository maintainers
- **Scope:** `ModelContextProtocol` 2.1.0 to 2.2.0 servicing and the public RoslynMcp transport/wire contract
- **Supersedes:** nothing; extends ADR 0003
- **Superseded by:** nothing

## Context

RoslynMcp pins the main `ModelContextProtocol` package and publishes a local stdio server. It does
not reference `ModelContextProtocol.AspNetCore`, and remote HTTP hosting remains deferred by
`http-streamable-host-project`.

The official 2.2.0 release adds `HttpServerSessionMode` to the separate ASP.NET Core package so
2025-11-25 stateful clients and 2026-07-28 stateless clients can share one HTTP endpoint. Its Core
runtime correction adds a minimum wrapper-length check to `McpHeaderEncoder.DecodeValue`, so a
degenerate `=?base64?=` header value no longer throws during substring extraction. RoslynMcp does
not call `McpHeaderEncoder`; stdio traffic has no MCP HTTP parameter headers.

The restored 2.2.0 package retains its Apache-2.0 license, target frameworks, and dependency shape.
The net10 XML member inventories for both `ModelContextProtocol` and `ModelContextProtocol.Core`
contain the same member identifiers as 2.1.0. Those source and API observations narrow the review,
but do not replace integration evidence against RoslynMcp's registered surface and both supported
protocol eras.

## Decision

1. Pin `ModelContextProtocol` 2.2.0.
2. Retain the stdio-only product boundary. Do not add `ModelContextProtocol.AspNetCore`, an HTTP
   endpoint, or `HttpServerSessionMode` as part of this servicing update.
3. Retain ADR 0003's supported protocol eras and negotiation rules:
   - initialization-based sessions through 2025-11-25;
   - discovery/request-scoped 2026-07-28 sessions.
4. Classify the update as non-breaking maintenance. Tool, prompt, resource, schema, result, error,
   elicitation, sampling, logging, and cache contracts do not intentionally change.
5. Require refreshed third-party notices, raw-wire coverage for both eras, surface registration and
   schema tests, the complete local release gate, and the hosted `validate` aggregate before merge.

## Public-change classification

| Concern | Classification | RoslynMcp disposition | Consumer migration |
|---|---|---|---|
| SDK package pin | Maintenance | Adopt 2.2.0 after complete validation. | None. |
| Hybrid stateful/stateless HTTP serving | Not adopted | The feature lives in the unreferenced ASP.NET Core package; RoslynMcp remains stdio-only. | None. |
| Degenerate MCP HTTP-header decoding | Upstream bug fix | The corrected Core helper is not called by RoslynMcp and does not alter stdio frames. | None. |
| Supported protocol eras | No change | Preserve ADR 0003's 2025-11-25 and 2026-07-28 contracts. | None. |

## Consumer migration

None. Existing clients continue using the stdio transport and the protocol-era behavior recorded in
ADR 0003. This update does not add an HTTP endpoint, alter discovery or initialization, or change a
RoslynMcp request or response contract.

## Consequences

- The direct SDK pin, notice inventory, and current-version documentation move to 2.2.0 together.
- Historical 1.4.1→2.1.0 evidence in ADR 0003 and existing regressions remains unchanged.
- HTTP hosting still requires a separate product decision and implementation.
- A later SDK release receives its own release-note, API, dependency, license, and wire review rather
  than inheriting this disposition.

## Validation

- Restore the exact package and verify its nuspec identity and Apache-2.0 license.
- Run all dual-era raw-wire suites.
- Run tool, prompt, and resource registration and schema suites.
- Run `just ci`.
- Require the hosted `validate` aggregate, including Windows, Linux, and exact-SDK-floor jobs.

## References

- [ADR 0003 — MCP SDK 2.x wire compatibility](0003-sdk-2x-wire-compatibility.md)
- [ModelContextProtocol 2.2.0 release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0)
- [ModelContextProtocol 2.1.0...2.2.0 source comparison](https://github.com/modelcontextprotocol/csharp-sdk/compare/v2.1.0...v2.2.0)
- [ModelContextProtocol 2.2.0 on NuGet](https://www.nuget.org/packages/ModelContextProtocol/2.2.0)
- [RoslynMcp release policy](../release-policy.md)
