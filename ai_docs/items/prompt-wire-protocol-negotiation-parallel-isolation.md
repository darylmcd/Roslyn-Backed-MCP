# prompt-wire-protocol-negotiation-parallel-isolation

## Anchors

- `tests/RoslynMcp.Tests/PromptCallErrorFilterTests.cs`
- `tests/RoslynMcp.Tests/Helpers/InMemoryMcpClientServerHarness.cs`
- `tests/RoslynMcp.Tests/WorkspacePathMrtrWireTests.cs`

## Acceptance

- Reproduce or stress the legacy plus latest protocol harnesses concurrently and identify whether SDK negotiation state crosses in-memory server sessions.
- Make protocol-era assertions deterministic under the repository's class-level parallel test policy; use `DoNotParallelize` only when the SDK state cannot be isolated per harness, and document that constraint.
- A repeated concurrency regression runs the affected prompt and tool wire harnesses together without a latest request negotiating `2025-11-25`.

## Evidence

- The first 2026-08-22 `just ci` run failed only `WrongTypedPromptArgument_UsesStableInvalidParamsWithoutReporting`: the latest-era iteration expected `2026-07-28` but negotiated `2025-11-25`.
- The exact Release test passed immediately in isolation, including both protocol eras.
- A second complete `just ci` run passed 2,445 tests with zero failures.

## Amendment — 2026-09-02 (second independent symptom, same root cause)

A second test hit this defect from a different assertion, confirming it is not specific to
`PromptCallErrorFilterTests`.

`ElicitationChoicePromptTests.SupportsElicitation_UsesRequestCapabilitiesForModern_AndServerSnapshotForLegacy`
failed once during a full-suite Release gate: `Assert.IsNull(modernHarness.Server.ClientCapabilities)`
observed a non-null `ClientCapabilities`. The harness retains connection-scoped client capabilities
ONLY for the legacy protocol version (`_legacyConnectionScopedProtocolVersion = "2025-11-25"`), so a
non-null value is the same "latest request negotiated 2025-11-25" outcome this row already records —
observed through capability retention rather than through an era assertion.

Add `tests/RoslynMcp.Tests/ElicitationChoicePromptTests.cs` to the anchors; the concurrency regression
in acceptance bullet 3 should cover it alongside the prompt and tool wire harnesses.

Reproduction profile gathered 2026-09-02, all on commit 3fcd132b:

| Shape | Result |
|---|---|
| Base, class in isolation, x3 | PASS |
| Branch, class in isolation, x3 | PASS |
| Branch, 3 interacting classes in one process, x5 | PASS |
| Branch, full suite, 2 sibling suites running | PASS (2818/0) |
| Branch, full suite, 4 concurrent full suites | **FAIL** |

That gradient matches this row's own evidence (fails in a loaded full run, passes immediately in
isolation) and strengthens the "state crosses in-memory server sessions under load" hypothesis.

Ruled out as a cause: the `output-schema-generation-authority` diff under which it surfaced.
`ElicitationChoicePromptTests` builds its server from a bare `McpServerOptions()` with no DI
container, while `SurfaceRegistrationPolicy.Apply` is reachable only through
`services.PostConfigure<McpServerOptions>()`.

Distinct from the registered `WorkspaceExecutionGateTests` timing flake and from concurrent-load
`TimeoutException`s: those are timeouts, this is a negotiation/capability assertion. Do not fold them
together.

[source: 2026-09-02 backlog-remediate triage]
Additional 2026-09-04 full-gate evidence: ServerDiscoveryWireTests returned protocol 2025-11-25 where 2026-07-28 was expected in two assertions (tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs:20-66,153-177). Both exact tests then passed three consecutive isolated runs. This is a third loaded-suite symptom of the same cross-session negotiation state row, not evidence that the actionlint change caused it.

## Amendment — 2026-09-04 (fourth independent loaded-suite symptom)

The final actionlint contract's unsharded Release gate failed only
`ElicitationChoicePromptTests.SupportsElicitation_UsesRequestCapabilitiesForModern_AndServerSnapshotForLegacy`:
the modern harness unexpectedly retained a non-null `ClientCapabilities` snapshot. The exact test passed
immediately in isolation (1/1 in 184 ms). The same run built with zero warnings/errors and passed 2,922
other tests, so this is further evidence of cross-session protocol negotiation state under a loaded suite,
not an actionlint regression.
