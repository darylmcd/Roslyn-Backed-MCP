# ADR 0031 — ModelContextProtocol 2.3 servicing

- **Status:** Accepted (2026-10-26)
- **Deciders:** repository maintainers
- **Scope:** `ModelContextProtocol` 2.2.0 to 2.3.0 servicing and the public RoslynMcp transport/wire contract
- **Supersedes:** the exact-2.2.0 version clause of ADR 0007 (its policy is otherwise unchanged)
- **Superseded by:** nothing

## Context

RoslynMcp pins the main `ModelContextProtocol` package and publishes a local stdio server. ADR 0006
recorded the 2.1.0→2.2.0 servicing review and required every later SDK release to receive its own
release-note, API, dependency, license, and wire review.

The official 2.3.0 release notes and source comparison list these changes:

| Upstream change | Surface | RoslynMcp relevance |
|---|---|---|
| `McpClient.ServerInfo` becomes `Implementation?` (#1903); 2026-07-28 discovery may omit optional server identity | Client API | RoslynMcp reads no `McpClient.ServerInfo`; it sets `McpServerOptions.ServerInfo` (non-null) in `src/RoslynMcp.Host.Stdio/Program.cs` and every test host. The server keeps advertising its identity. |
| Request-scoped capability and discovery-advertisement corrections; opaque legacy metadata accepted (#1852) | Server, both eras | Exercised by the dual-era raw-wire suites. |
| Insertion order preserved when listing tools, prompts, and resources (#1924) | Server | `tools/list`, `prompts/list`, and `resources/list` follow registration order. RoslynMcp does not document or test a list order; MCP does not define one. |
| Stdio client closes server stdin before waiting for exit (#1838) | Client stdio transport | Affects test harnesses that launch the server as a child; server shutdown ownership is unchanged. |
| Streamable HTTP status mapping, SSE fallback, OAuth probe budget, HTTP 400 parse details, SSE POST disposal (#1791, #1837, #1844, #1855, #1719, #1841) | HTTP client/server | Not used: RoslynMcp is stdio-only and does not reference `ModelContextProtocol.AspNetCore`. |
| `McpClientOptions.TimeProvider` (#1719), numeric progress-token serialization (#1832), protocol-version telemetry (#1810) | Client / telemetry | No RoslynMcp contract change. |

Package review of the restored 2.3.0 artifacts:

- `ModelContextProtocol` and `ModelContextProtocol.Core` keep the Apache-2.0 license expression and
  the net10.0 target used by RoslynMcp.
- Core dependency floors move from 10.0.10 to 10.0.13 for the `System.*`, `Microsoft.Bcl.Memory`,
  and logging/hosting/caching abstraction packages, `Microsoft.Extensions.AI.Abstractions` moves
  from 10.8.3 to 10.11.0, and `Microsoft.Bcl.TimeProvider` 8.0.1 is added. RoslynMcp's direct
  Extensions pins are already at or above those floors.
- The SDK stdio server transport still owns a `BufferedStream` over standard output at v2.3.0, so
  the shutdown-flush ownership comment in `Program.cs` remains correct.
- `ModelContextProtocol.Extensions.Tasks` 2.3.0 exists and declares an exact
  `ModelContextProtocol (= 2.3.0)` dependency.

## Decision

1. Pin `ModelContextProtocol` 2.3.0.
2. Retain the stdio-only product boundary. Do not add `ModelContextProtocol.AspNetCore` or any HTTP
   endpoint as part of this servicing update.
3. Retain ADR 0003's supported protocol eras and negotiation rules:
   - initialization-based sessions through 2025-11-25;
   - discovery/request-scoped 2026-07-28 sessions.
4. Keep advertising a non-null `ServerInfo` on both eras. The SDK's optional-identity allowance is a
   client-side tolerance and does not change what RoslynMcp sends.
5. Classify the update as non-breaking maintenance. Tool, prompt, resource, schema, result, error,
   elicitation, sampling, logging, and cache contracts do not intentionally change. List order was
   never a RoslynMcp contract.
6. Amend ADR 0007: the later Tasks adoption uses the exact `ModelContextProtocol.Extensions.Tasks`
   / `ModelContextProtocol` pair that matches the pinned core (2.3.0 / 2.3.0 under this ADR) and
   re-verifies that pair when it lands. All other ADR 0007 policy is unchanged.
7. Require refreshed third-party notices and upgrade matrix, raw-wire coverage for both eras,
   surface registration and schema tests, and the hosted `validate` aggregate before merge.

## Public-change classification

| Concern | Classification | RoslynMcp disposition | Consumer migration |
|---|---|---|---|
| SDK package pin | Maintenance | Adopt 2.3.0 after complete validation. | None. |
| Nullable client `ServerInfo` | Upstream client API correction | No RoslynMcp code reads it; the server still sends identity. | None. A client built on the 2.3 SDK that reads `ServerInfo` must already handle null for other servers. |
| Registration-order listing | Upstream behavior correction | Accepted; order is not part of the RoslynMcp contract. | None. Clients must not depend on list order. |
| Discovery / capability advertisement correction | Upstream bug fix | Covered by the dual-era raw-wire suites. | None. |
| HTTP, SSE, and OAuth changes | Not adopted | Outside this stdio product. | None. |
| Supported protocol eras | No change | Preserve ADR 0003's 2025-11-25 and 2026-07-28 contracts. | None. |
| Tasks extension pair | Amended | ADR 0007's later adoption targets the 2.3.0 / 2.3.0 pair. | None; Tasks are not yet wired. |

## Consumer migration

None. Existing clients continue using the stdio transport and the protocol-era behavior recorded in
ADR 0003. This update does not add an HTTP endpoint, alter discovery or initialization semantics, or
change a RoslynMcp request or response contract.

## Consequences

- The direct SDK pin, notice inventory, upgrade matrix, and current-version documentation move to
  2.3.0 together.
- ADR 0006 remains the historical 2.1.0→2.2.0 record.
- A later SDK release receives its own release-note, API, dependency, license, and wire review
  rather than inheriting this disposition.

## Validation

- Restore the exact packages and verify their nuspec identity, Apache-2.0 license, and dependency
  shape (done for this ADR; see Context).
- Run the third-party notice and upgrade-matrix parity gates.
- Run all dual-era raw-wire suites and the tool, prompt, and resource registration and schema suites
  through the hosted `validate` aggregate, including Windows, Linux, and exact-SDK-floor jobs.

## References

- [ADR 0003 — MCP SDK 2.x wire compatibility](0003-sdk-2x-wire-compatibility.md)
- [ADR 0006 — ModelContextProtocol 2.2 servicing](0006-modelcontextprotocol-2-2-servicing.md)
- [ADR 0007 — MCP Tasks extension compatibility](0007-tasks-extension-compatibility.md)
- [ModelContextProtocol 2.3.0 release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.3.0)
- [ModelContextProtocol 2.2.0...2.3.0 source comparison](https://github.com/modelcontextprotocol/csharp-sdk/compare/v2.2.0...v2.3.0)
- [ModelContextProtocol 2.3.0 on NuGet](https://www.nuget.org/packages/ModelContextProtocol/2.3.0)
- [RoslynMcp release policy](../release-policy.md)
