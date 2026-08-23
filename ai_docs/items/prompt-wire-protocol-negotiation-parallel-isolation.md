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
