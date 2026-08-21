# ADR 0003 — MCP SDK 2.x wire compatibility

- **Status:** Accepted (2026-08-14)
- **Deciders:** repository maintainers
- **Scope:** `ModelContextProtocol` 1.4.1 to 2.1.0 adoption and the public RoslynMcp wire contract
- **Supersedes:** undocumented assumptions in the original SDK-upgrade release record
- **Superseded by:** nothing

## Context

Commit [`e0e21c3f`](https://github.com/darylmcd/Roslyn-Backed-MCP/commit/e0e21c3f0ce77d47b6ec9179f709502083da0e4f)
changed this repository's `ModelContextProtocol` pin directly from 1.4.1 to 2.1.0. The repository did
not adopt an SDK 2.0.1 release, and there is no SDK 3.x in this migration lineage. RoslynMcp's own
2.x and 3.x product releases are a separate version series.

The official package sequence between the two pins was 2.0.0-preview.1, preview.2, preview.3,
rc.1, rc.2, and 2.0.0. We evaluate those releases even though the checkout did not pin them. The
publish chronology overlaps the 1.x servicing line: preview.1 shipped on 2026-06-26, before 1.4.1
shipped on 2026-07-09; preview.2 followed later that same day. The authoritative package list is the
[NuGet flat-container version index](https://api.nuget.org/v3-flatcontainer/modelcontextprotocol/index.json);
later SDK releases, including 2.2.0, are outside this adoption decision.

The structured-result regression was not caused by the package bump alone. Commit `e0e21c3f`
introduced SDK 2.1's CLR-return-value projection. Commit
[`641e8c96`](https://github.com/darylmcd/Roslyn-Backed-MCP/commit/641e8c96e0bb37562733c65e0346c08e6639ecc7)
then registered output schemas for methods that still returned pre-serialized JSON strings. Together,
those changes caused the SDK to publish the serialized JSON as a JSON string in `structuredContent`.

The 2026-08-13 conformance report is historical evidence, not proof of the upgraded checkout. Its
live probe used the installed RoslynMcp 2.3.8 binary with protocol 2025-06-18 before PR #1223, and
the client's advertised capabilities were not retained. Its source inventory examined a checkout
pinned to SDK 2.1.0. Current compatibility claims require new raw-wire evidence against the built
checkout and each supported protocol era; no capability-dependent conclusion from the old probe is
portable.

## Evaluated SDK lineage

| SDK release | Published (UTC) | Schema and structured results | Protocol, capabilities, and server info | Cache and result fields | Logging, elicitation, sampling, and MRTR | Tasks |
|---|---|---|---|---|---|---|
| [1.4.1](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v1.4.1) | 2026-07-09 | Legacy baseline: object-oriented structured-result conventions; deserialization tolerated a missing `Tool.inputSchema`. | Stateful `initialize` session; identity and capabilities established during initialization. | No 2026-07-28 cache/result contract. | Direct server-to-client Logging, Elicitation, Roots, and Sampling APIs. No portable MRTR contract. | Experimental in the core SDK surface. |
| [2.0.0-preview.1](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0-preview.1) | 2026-06-26 | Requires `Tool.inputSchema`; accepts JSON Schema 2020-12 output schemas; modern non-object results use their natural JSON shape. | Adds `server/discover`, stateless operation, and per-request protocol/client metadata while retaining down-level initialization. | Introduces cache hints, initially with incomplete conformance coverage. | Deprecates legacy Roots, Sampling, and Logging for the modern era; adds MRTR so request-scoped input can work in both eras through compatibility translation. | Replaces the earlier experimental design; initially remains in the main SDK packages. |
| [2.0.0-preview.2](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0-preview.2) | 2026-07-09 | Stabilizes protocol serialization properties; no new result-shape break relative to preview.1. | No negotiation-model change. | Adds diagnostics and documentation for non-conforming cacheable results. | No model change. | No model change. |
| [2.0.0-preview.3](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0-preview.3) | 2026-07-15 | Adds the `resultType` discriminator and related correctness fixes. | Fixes request-scoped capabilities and tightens dual-era negotiation. | Continues the draft 2026 result contract. | Retains modern MRTR and legacy compatibility behavior. | Moves Tasks into the separate `ModelContextProtocol.Extensions.Tasks` package. |
| [2.0.0-rc.1](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0-rc.1) | 2026-07-25 | No new schema-shape change. | Rejects mismatched initialization versions. | No new cache-shape change. | No new model change. | Fixes HTTP filter composition and removes a preview-only task-scope helper. |
| [2.0.0-rc.2](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0-rc.2) | 2026-07-28 | Keeps natural modern result shapes. | Moves modern server information to request `_meta`; aligns modern method and status boundaries. | Gates 2026-07-28 `resultType`, `ttlMs`, and `cacheScope` fields by negotiated protocol. | Modern requests use `_meta` log level and MRTR rather than legacy logging or nested server requests; legacy connections retain their compatibility surface. | Adds configurable execution modes and completes conformance coverage. |
| [2.0.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0) | 2026-07-28 | General-availability form of the rc.2 dual-era schema/result contract. | Discovery-first modern negotiation with down-level initialization interoperability. | General-availability protocol-gated cache/result fields. | Roots, Sampling, and Logging remain deprecated; MRTR is stable. | Separate extension package; no wire/API compatibility with the earlier experimental Tasks design. |
| [2.1.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.1.0) | 2026-08-05 | No new schema/result break relative to 2.0.0. | Improves HTTP discovery fallback and adds opt-in `subscriptions/listen`. | No new cache/result break. | No new elicitation, sampling, or logging model. | No model change. |

## Decision

1. RoslynMcp supports two explicit wire eras while pinned to SDK 2.1.0:
   - legacy, initialization-based sessions through protocol 2025-11-25;
   - modern 2026-07-28 requests using `server/discover` and request-scoped metadata.
2. Protocol-version-dependent fields and error codes are selected from the request's negotiated
   protocol context. They are never emitted optimistically and never inferred from the RoslynMcp
   product version.
3. A schema-declaring tool constructs an explicit producer-owned `CallToolResult` from one typed
   DTO. The same serialized value populates its legacy text and structured channels, and the SDK
   accepts that explicit envelope without synthesizing or re-serializing `structuredContent`.
   Text projection may decorate the text fallback, but it must preserve producer-owned result and
   content-block fields and must never create, reconstruct, or overwrite `structuredContent`.
4. **Target requirement (tracked below):** failures must use their protocol-defined boundary. Tool
   execution failures use `CallToolResult.isError`; resource/request failures use JSON-RPC errors.
   Public responses must contain stable, secret-safe summaries, while server diagnostics retain only
   correlation and benign diagnostic structure. Raw user-derived or secret-bearing values must never
   be persisted or sent on the MCP wire.
5. Modern input recovery uses request-scoped MRTR. Direct nested Elicitation remains only for the
   stateful 2025-11-25 compatibility leg. Sampling is MRTR-only; legacy or sampling-incapable
   requests receive the deterministic product fallback instead of a nested sampling request. Tasks
   are not part of the base SDK adoption and require an explicit dependency on
   `ModelContextProtocol.Extensions.Tasks` plus a separate product decision.
6. No SDK-current compatibility claim is accepted from source inspection, an installed older binary,
   or test counts alone. Raw-wire tests must exercise every affected endpoint under both supported
   protocol eras.

SDK 2.1 keeps its protocol-date feature helper internal. RoslynMcp therefore owns one compatibility
seam that applies the SDK's ISO-date ordinal rule to the request protocol first and the negotiated
session protocol only as a stateful fallback. No filter caches that value globally.

## Public-change classification and delivery state

This table is a durable snapshot of the decision at acceptance. “Delivered” cites code and tests that
remain after backlog closure. “Tracked” names the current planning handle for work that this ADR does
not claim is implemented; when that work ships, its changelog entry and durable regression replace
the planning handle as delivery evidence.

| Concern | Release-policy classification | State and durable evidence / planning handle | Consumer migration |
|---|---|---|---|
| Schema-tool `structuredContent` plus lossless text projection | Breaking stable-response correction | **Delivered:** `StructuredToolResult`, `StructuredCallContentProjector`, `StructuredCallContentProjectorTests`, and `StructuredContentWireContractTests` | Deserialize `structuredContent` according to the advertised output schema. Do not parse a JSON document from a JSON string. The text channel remains a fallback, not the typed contract. |
| Synthetic non-object result-shape compatibility | Test-only follow-up | **Tracked:** `protocol-version-result-shape-wire-contract` | No current object-schema migration. This follow-up guards natural modern array/scalar shapes and their legacy envelope translation. |
| Legacy cache/result-field leakage | Breaking dual-protocol correction | **Delivered:** `RequestProtocolFeatureGate`, `StaticListResultFilter`, `ResourceReadResultFilter`, and `ServerDiscoveryWireTests` | On legacy sessions, treat `resultType`, `ttlMs`, and `cacheScope` as absent. On 2026-07-28, honor the required result discriminator and cache policy. |
| Resource failures encoded as successful bodies | Breaking stable-behavior correction | **Delivered:** `ResourceReadResultFilter`, `ResourceReadErrorPolicy`, and `ResourceReadWireContractTests` cover workspace and server-catalog resources in both supported protocol eras. | Handle `resources/read` failures through JSON-RPC errors. Expect legacy missing-resource `-32002` and modern `InvalidParams` (`-32602`) according to the negotiated protocol. |
| Protocol logging bridge and capability retirement | Breaking capability/notification correction | **Delivered:** `RequestCorrelationMessageFilter`, `ServerObservabilityReporter`, `McpLoggingLifecycleWireTests`, and `ServerObservabilitySinkTests` | Stop depending on `logging/setLevel` or `notifications/message` from RoslynMcp. Use client-side diagnostics plus operator-controlled stderr output; opt into secret-safe structured events with `ROSLYNMCP_OBSERVABILITY_SINK=stderr`. |
| Direct Elicitation replaced by request-scoped MRTR | Breaking interaction correction | **Delivered:** `RequestScopedInputAdapter`, `RequestStateCodec`, `StructuredCallElicitationCoordinator`, `ElicitationChoicePrompt`, `WorkspacePathMrtrWireTests`, `SymbolDisambiguationMrtrWireTests`, and the real-handle `SymbolDisambiguationElicitationTests` retry regression | Support input requests and retry with input responses in the request scope. Treat server-provided `requestState` as opaque and echo it byte-for-byte unchanged so multi-stage retries stay bound to the selected workspace. Do not require the server to initiate `elicitation/create` on modern sessions. The stateful 2025-11-25 leg retains direct form elicitation. |
| Legacy Sampling replaced by request-scoped MRTR input | Breaking interaction correction | **Delivered:** `RequestScopedInputAdapter`, `McpSamplingTestNameSuggestionProvider`, and `SamplingMrtrWireTests` cover modern round trips, malformed/cancelled responses, capability refusal, and secret-safe fallback | Supply sampling input responses when offered on 2026-07-28 requests. Legacy or sampling-incapable requests deterministically use the placeholder; no nested `sampling/createMessage` compatibility leg remains. |
| Tasks for slow operations | Additive, opt-in extension | **Tracked:** `tasks-extension-slow-ops` | No migration until enabled. Adoption requires the separate Tasks package and a client that negotiates the extension; existing synchronous calls remain valid. |
| Cohesion, reflection, DI, exception-flow, NuGet, and code-fix completeness fields | Additive stable-response evolution | **Tracked:** `cohesion-scan-completeness-contract`, `reflection-usage-scan-completeness`, `di-registration-scan-completeness`, `exception-flow-scan-completeness`, `nuget-dependency-scan-completeness`, `diagnostic-codefix-enumeration-completeness` | Ignore unknown fields on older clients. New clients must inspect completeness/failure counts before treating totals as exhaustive. |
| Raw exception detail in tool, prompt, coverage, scaffolding, analyzer, reference, workspace-readiness, workspace-validation, validation-command execution, composite-apply, FixAll, sampling, and cleanup responses | Breaking security correction; no deprecation window for secrets | **Partially delivered:** the shared tool boundary plus `GetPromptErrorFilter`, retired prompt-handler catches, prompt-shim binding policy, validation/test/build command projection, coverage, scaffolding IO, analyzer load, bulk reference, workspace validation/readiness, composite apply, FixAll provider failures, and request-scoped sampling have focused sentinel regressions. Cleanup and newly discovered adjacent surfaces remain tracked. | Stop parsing exception text, exception types, stack traces, supplied values, command arguments, filters, or paths. Branch only on documented categories/statuses and use a correlation identifier for operator-side diagnosis. Expected validation/not-found messages are stable guidance, not exception-text mirrors. |
| Unauthorized access classified as an internal failure | Breaking stable-error correction | **Delivered:** `ToolErrorHandler` maps `UnauthorizedAccessException` to secret-safe `PermissionDenied`; `BacklogFixTests` and `WorkspacePathMrtrWireTests` distinguish it from production sanctioned-root `InvalidArgument` refusal | Branch on `PermissionDenied` for access-policy or operating-system denial. Treat sanctioned-root parameter refusal as `InvalidArgument`; do not infer either category from free-form text. |
| Workspace lifecycle emits false resource-list changes | Non-breaking behavior correction | **Delivered:** static workspace lifecycle notification calls removed and `WorkspaceResourceListNotificationWireTests` proves byte-equivalent legacy/modern lists with no list-changed frames | Refresh `resources/list` only for an advertised list-change notification; do not rely on workspace load/reload/close to produce one. |

The delivered disclosure slice is owned by `tool-error-envelope-sensitive-detail-disclosure`,
`prompt-call-error-filter-boundary`, `prompt-error-catch-retirement-core-analysis`,
`prompt-error-catch-retirement-refactoring-guided`, `prompt-shim-binding-error-detail-redaction`,
`test-run-execution-dto-argv-path-disclosure`, `resource-read-protocol-error-semantics`,
`test-coverage-unexpected-error-detail-redaction`, `scaffolding-io-warning-detail-redaction`,
`analyzer-load-error-detail-redaction`, `bulk-reference-error-detail-redaction`,
`workspace-validation-error-detail-redaction`, `workspace-readiness-probe-error-redaction`,
`composite-apply-error-detail-redaction`, and `fixall-provider-error-detail-redaction`.
`mcp-sampling-mrtr-migration` adds the delivered sampling evidence above.
`atomic-file-cleanup-error-detail-redaction` and the bounded adjacent-review rows remain tracked;
this ADR does not claim those surfaces are implemented.

## Migration examples for breaking corrections

The examples describe the target contract. A tracked row must pass its wire regression before release
notes may claim that target is shipped.

### Structured results

```jsonc
// Before: the JSON document was encoded as a JSON string.
{ "structuredContent": "{\"state\":\"ready\"}" }

// Target: the value validates directly against the advertised schema.
{ "structuredContent": { "state": "ready" } }
```

Read the JSON value directly. Do not apply a second JSON parse to `structuredContent`.

### Protocol-dependent cache and result fields

```jsonc
// Legacy initialize session: omit modern-only fields.
{ "tools": [] }

// 2026-07-28 response: retain the negotiated result/cache contract.
{ "tools": [], "resultType": "complete", "ttlMs": 300000, "cacheScope": "private" }
```

Treat these fields as negotiated protocol features, not as invariant RoslynMcp fields.

### Resource errors

```jsonc
// Before: JSON error data inside a successful resources/read body.
{ "result": { "contents": [{ "text": "{\"error\":\"not found\"}" }] } }

// Target: JSON-RPC error channel; the exact missing-resource code is protocol-dependent.
{ "error": { "code": -32602, "message": "Resource not found" } }
```

Handle the request as failed and do not deserialize the successful resource-content contract.

### Logging

Before, a client could expect `logging/setLevel` and unsolicited `notifications/message`. Those are
no longer RoslynMcp's operational logging transport. Capture client request failures locally and use
operator-configured stderr for server diagnostics; set `ROSLYNMCP_OBSERVABILITY_SINK=stderr` only
when secret-safe structured unexpected-failure events are desired.

### Elicitation and sampling

Before, server code could initiate nested `elicitation/create` or `sampling/createMessage` requests.
On 2026-07-28, a call declares request-scoped input needs and the client retries with the matching
input responses. When an input-required result includes `requestState`, the client must neither
inspect nor modify it: echo the exact opaque string on the retry. RoslynMcp uses that state only to
preserve the already client-visible workspace id across stateless or concurrent retries; malformed
or foreign state is ignored, is never authorization, and the normal workspace lookup remains
authoritative. Stateful 2025-11-25 sessions retain direct form elicitation, but sampling no longer
uses a nested compatibility request: legacy or sampling-incapable calls receive the deterministic
placeholder. Clients that want sampled suggestions must support the 2026-07-28 MRTR input flow.

### Public error detail

Before, clients could observe implementation exception messages, paths, or stacks. After the tracked
security corrections, clients receive a stable category/summary and, where available, a correlation
identifier. Treat free-form message text as non-contractual and use the identifier for server-side
diagnosis. `UnauthorizedAccessException` now maps to `PermissionDenied`; sanctioned-root parameter
refusal remains `InvalidArgument`.

The delivered tool-boundary correction also stops echoing expected exception messages. Clients that
previously extracted workspace paths, missing keys, invalid values, preview tokens, or transport text
from `message` must retain that state locally and use `category`, `paramName`, stable remediation, and
`correlationId` instead. Composite-apply recovery continues to use the exact `appliedFiles` list; its
failing target is now a stable mutation ordinal rather than an unrestricted path.

Prompt failures now use the JSON-RPC error channel rather than a successful prompt message. Clients
must handle `prompts/get` errors and must not treat failure text as model input. Validation and build
execution records retain their established property names, counts, statuses, and diagnostic fields,
but absolute target/working/results paths and caller-supplied test filters are projected to stable
public values. Clients must not depend on the prior raw `arguments`, `targetPath`, or
`workingDirectory` values and should use the surrounding validation result for workflow decisions.

## Consequences

**Positive**

- The public migration record now separates SDK versions, protocol versions, and RoslynMcp versions.
- Compatibility work is dependency-ordered and cannot be declared complete from a single protocol
  probe or an older installed binary.
- Security corrections are classified honestly even when they intentionally remove observable detail.

**Negative**

- Clients that depended on malformed structured results, modern fields on legacy sessions, nested
  server requests, protocol log notifications, or raw exception text must migrate.
- Dual-era raw-wire coverage remains mandatory until the legacy protocol is retired under the normal
  public deprecation policy.

## References

- [C# SDK 2.0.0 release and migration notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0)
- [C# SDK 2.1.0 release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.1.0)
- [C# SDK 2.1 elicitation and MRTR guidance](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.1.0/docs/concepts/elicitation/elicitation.md)
- [RoslynMcp release policy](../release-policy.md)
- [Historical conformance report](../../ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md)
