# protocol-version-result-shape-wire-contract — Lock dual-protocol result shapes on raw wire

**row:** `protocol-version-result-shape-wire-contract` · **pri:** `High` · **size:** `S`

## Anchors

- New `tests/RoslynMcp.Tests/ProtocolVersionResultShapeWireTests.cs`.
- `tests/RoslynMcp.Tests/Helpers/InMemoryMcpClientServerHarness.cs`

## Acceptance

- [ ] Register one synthetic array-schema tool whose producer sets top-level `Meta`/`ResultType` and text-block `Meta`/`Annotations` sentinels.
- [ ] Under 2025-11-25, assert the legacy object envelope, omitted modern-only result discriminator, and lossless sentinels on the raw frame.
- [ ] Under 2026-07-28 `server/discover`, assert natural array `structuredContent`, required complete result discriminator, and the same lossless sentinels.
- [ ] Deep-validate each value against the protocol-appropriate advertised output schema.
- [ ] Keep the production all-eight object-schema contract anchored in `StructuredToolResult` and `StructuredContentWireContractTests`; this row adds only the synthetic non-object matrix.

## Evidence

- SDK 2.1 intentionally uses different structured-result/output-schema shapes by negotiated protocol for non-object schemas.
- Current worktree tests never observe this dual shape and do not detect dropped `Result.Meta`, `ResultType`, or block metadata/annotations.
