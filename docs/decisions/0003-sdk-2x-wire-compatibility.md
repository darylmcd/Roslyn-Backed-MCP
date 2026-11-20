# ADR 0003 — MCP SDK 2.x wire compatibility

- **Status:** Accepted (2026-08-14); leak and correction classification amended by [ADR 0041](0041-stable-corrections-and-security-exception.md)
- **Deciders:** repository maintainers
- **Scope:** `ModelContextProtocol` 1.4.1 to 2.1.0 adoption and the public RoslynMcp wire contract
- **Supersedes:** undocumented assumptions in the original SDK-upgrade release record
- **Superseded by:** nothing

Full history (per-release lineage, long migration prose): `git log -- docs/decisions/0003-sdk-2x-wire-compatibility.md`.

## Context

Commit [`e0e21c3f`](https://github.com/darylmcd/Roslyn-Backed-MCP/commit/e0e21c3f0ce77d47b6ec9179f709502083da0e4f) moved the `ModelContextProtocol` pin directly from 1.4.1 to 2.1.0. There is no SDK 2.0.1 or 3.x in this lineage; RoslynMcp's own 2.x/3.x releases are a separate series. The intermediate releases (2.0.0-preview.1/2/3, rc.1, rc.2, 2.0.0; [NuGet index](https://api.nuget.org/v3-flatcontainer/modelcontextprotocol/index.json)) are evaluated though never pinned. Releases from 2.2.0 onward are out of scope.

The structured-result regression came from two commits: `e0e21c3f` (SDK 2.1 CLR-return projection) and [`641e8c96`](https://github.com/darylmcd/Roslyn-Backed-MCP/commit/641e8c96e0bb37562733c65e0346c08e6639ecc7) (output schemas registered for methods still returning pre-serialized JSON strings), so the SDK published that JSON as a string in `structuredContent`.

The 2026-08-13 [conformance report](../../ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md) is historical only: its live probe used an installed 2.3.8 binary on protocol 2025-06-18 with no retained client capabilities. Current claims need fresh raw-wire evidence against the built checkout in each supported era.

### SDK lineage (what changed between the pins)

| SDK release | Date | Change relevant to RoslynMcp |
|---|---|---|
| 1.4.1 | 2026-07-09 | Baseline: stateful `initialize` sessions; direct server-to-client Logging, Elicitation, Roots, Sampling; Tasks experimental in core; no 2026-07-28 cache/result contract. |
| 2.0.0-preview.1 to rc.2 | 2026-06-26 to 07-28 | Requires `Tool.inputSchema`; 2020-12 output schemas with natural non-object shapes; `server/discover`, stateless operation, per-request metadata and `_meta` server info; `resultType`, `ttlMs`, `cacheScope` gated by negotiated protocol; legacy Roots/Sampling/Logging deprecated for the modern era in favor of MRTR; Tasks moved to `ModelContextProtocol.Extensions.Tasks`. |
| 2.0.0 | 2026-07-28 | GA of the dual-era contract: discovery-first negotiation with down-level initialization; MRTR stable; Tasks extension incompatible with the earlier experimental design. |
| 2.1.0 | 2026-08-05 | No result/schema break; better HTTP discovery fallback; opt-in `subscriptions/listen`. |

## Decision

1. RoslynMcp supports two explicit wire eras while pinned to SDK 2.1.0: legacy initialization-based sessions through protocol 2025-11-25, and modern 2026-07-28 requests using `server/discover` and request-scoped metadata.
2. Protocol-dependent fields and error codes come from the request's negotiated protocol context. They are never emitted optimistically and never inferred from the RoslynMcp product version.
3. A schema-declaring tool builds an explicit producer-owned `CallToolResult` from one typed DTO. The same serialized value fills the legacy text and structured channels; the SDK accepts that envelope without synthesizing or re-serializing `structuredContent`. Text projection may decorate the text fallback but never creates or overwrites `structuredContent`.
4. **Target requirement (delivery tracked below):** failures use their protocol boundary. Tool execution failures use `CallToolResult.isError`; resource/request failures use JSON-RPC errors. Public responses carry stable, secret-safe summaries; server diagnostics keep only correlation and benign structure. Raw user-derived or secret-bearing values are never persisted or sent on the wire.
5. Modern input recovery uses request-scoped MRTR. Direct nested Elicitation remains only on the stateful 2025-11-25 leg. Sampling is MRTR-only; legacy or sampling-incapable requests get the deterministic product fallback, not a nested sampling request. Tasks need an explicit `ModelContextProtocol.Extensions.Tasks` dependency plus a separate product decision.
6. No SDK-current compatibility claim is accepted from source inspection, an older installed binary, or test counts alone. Raw-wire tests must exercise every affected endpoint in both eras.

SDK 2.1 keeps its protocol-date helper internal, so RoslynMcp owns one seam applying the SDK's ISO-date ordinal rule to the request protocol first and the session protocol only as a stateful fallback; no filter caches it globally.

## Public-change classification and delivery state

Snapshot at acceptance. "Delivered" cites code and tests that outlive backlog closure; "Tracked" names a planning handle for work not claimed as implemented.

| Concern | Classification | State and evidence / handle | Consumer migration |
|---|---|---|---|
| Schema-tool `structuredContent` plus lossless text projection | Breaking stable-response correction | **Delivered:** `StructuredToolResult`, `StructuredCallContentProjector`, `StructuredContentWireContractTests` | Read `structuredContent` as a JSON value per the output schema; never JSON-parse it a second time. Text is a fallback, not the typed contract. |
| Non-object result-shape compatibility | Test-only follow-up | **Tracked:** `protocol-version-result-shape-wire-contract` | None for object schemas; guards natural modern array/scalar shapes and legacy envelope translation. |
| Legacy cache/result-field leakage | Breaking dual-protocol correction | **Delivered:** `RequestProtocolFeatureGate`, `StaticListResultFilter`, `ResourceReadResultFilter`, `ServerDiscoveryWireTests` | Legacy sessions: treat `resultType`, `ttlMs`, `cacheScope` as absent. 2026-07-28 (e.g. `"resultType": "complete", "ttlMs": 300000, "cacheScope": "private"`): honor discriminator and cache policy. |
| Resource failures as successful bodies | Breaking stable-behavior correction | **Delivered:** `ResourceReadResultFilter`, `ResourceReadErrorPolicy`, `ResourceReadWireContractTests` (both eras) | Handle `resources/read` failures as JSON-RPC errors: legacy missing-resource `-32002`, modern `InvalidParams` (`-32602`). |
| Logging bridge and capability retirement | Breaking capability correction | **Delivered:** `RequestCorrelationMessageFilter`, `ServerObservabilityReporter`, `McpLoggingLifecycleWireTests`, `ServerObservabilitySinkTests` | Stop relying on `logging/setLevel` or `notifications/message`; use client diagnostics and operator stderr; opt into secret-safe events with `ROSLYNMCP_OBSERVABILITY_SINK=stderr`. |
| Direct Elicitation replaced by MRTR | Breaking interaction correction | **Delivered:** `RequestScopedInputAdapter`, `RequestStateCodec`, `StructuredCallElicitationCoordinator`, `WorkspacePathMrtrWireTests`, `SymbolDisambiguationMrtrWireTests` | Answer input requests by retrying with input responses. Echo `requestState` byte-for-byte. Do not expect server-initiated `elicitation/create` on modern sessions; the 2025-11-25 leg keeps direct form elicitation. |
| Legacy Sampling replaced by MRTR input | Breaking interaction correction | **Delivered:** `RequestScopedInputAdapter`, `McpSamplingTestNameSuggestionProvider`, `SamplingMrtrWireTests` | Supply sampling input responses on 2026-07-28 requests; legacy or sampling-incapable requests get the deterministic placeholder (no nested `sampling/createMessage`). |
| Tasks for slow operations | Additive, opt-in | **Decision delivered:** [ADR 0007](0007-tasks-extension-compatibility.md). **Tracked:** `tasks-extension-workspace-load`, `tasks-extension-build-test-run`, `tasks-extension-contract-docs` | None until enabled; synchronous calls stay valid (terms in ADR 0007). |
| Completeness fields (cohesion, reflection, DI, exception-flow, NuGet, code-fix) | Additive stable-response evolution | **Tracked:** `cohesion-scan-completeness-contract`, `reflection-usage-scan-completeness`, `di-registration-scan-completeness`, `exception-flow-scan-completeness`, `nuget-dependency-scan-completeness`, `diagnostic-codefix-enumeration-completeness` | Check completeness/failure counts before treating totals as exhaustive; older clients ignore the fields. |
| Raw exception detail in tool, prompt, coverage, scaffolding, analyzer, reference, readiness, validation, command, composite-apply, FixAll, sampling and cleanup responses | Breaking security correction; no deprecation window for secrets | **Partially delivered:** shared tool boundary, `GetPromptErrorFilter`, prompt catch retirement and shim binding policy, command projection, coverage, scaffolding IO, analyzer load, bulk reference, validation/readiness, composite apply, FixAll and sampling have sentinel regressions (rows `tool-error-envelope-sensitive-detail-disclosure`, `prompt-call-error-filter-boundary` and the matching `*-error-detail-redaction` rows). **Tracked:** `atomic-file-cleanup-error-detail-redaction` and adjacent-review rows. | Stop parsing exception text, types, stacks, values, arguments, filters or paths. Branch on documented `category`, `paramName`, status and stable remediation; use `correlationId` for diagnosis. Composite-apply keeps the exact `appliedFiles` list; its failing target is a stable mutation ordinal. |
| Unauthorized access as internal failure | Breaking stable-error correction | **Delivered:** `ToolErrorHandler` maps `UnauthorizedAccessException` to `PermissionDenied`; `BacklogFixTests`, `WorkspacePathMrtrWireTests` | `PermissionDenied` = access-policy/OS denial; sanctioned-root parameter refusal stays `InvalidArgument`. Do not infer either from text. |
| False resource-list changes on workspace lifecycle | Non-breaking correction | **Delivered:** notification calls removed; `WorkspaceResourceListNotificationWireTests` | Refresh `resources/list` only for an advertised list-change notification. |

## Migration notes for breaking corrections

The table's migration column is normative; these add only the points it omits. A tracked row must pass its wire regression before release notes claim it shipped.

- **Structured results.** Before: `{"structuredContent": "{\"state\":\"ready\"}"}` (a JSON string). Target: `{"structuredContent": {"state": "ready"}}`.
- **`requestState`.** Only preserves the client-visible workspace id across stateless retries; malformed or foreign state is ignored, never authorization, and normal workspace lookup stays authoritative.
- **Public error detail.** `prompts/get` failures use the JSON-RPC error channel, never a successful prompt message. Validation and build execution records keep property names, counts and statuses, but absolute target/working/results paths and caller-supplied test filters are projected to stable values; stop using raw `arguments`, `targetPath` or `workingDirectory`.

## Consequences

**Positive:** SDK, protocol and RoslynMcp versions are kept separate; compatibility cannot be declared complete from one probe or an old binary; security corrections are classified honestly even when they remove detail.

**Negative:** clients relying on malformed structured results, modern fields on legacy sessions, nested server requests, log notifications or raw exception text must migrate; dual-era raw-wire coverage stays mandatory until the legacy protocol is retired under the deprecation policy.

## References

- C# SDK [2.0.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0) and [2.1.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.1.0) release notes; [elicitation and MRTR guidance](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.1.0/docs/concepts/elicitation/elicitation.md)
- [RoslynMcp release policy](../release-policy.md)
