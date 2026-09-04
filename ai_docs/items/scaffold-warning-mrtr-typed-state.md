# scaffold-warning-mrtr-typed-state — Carry typed sibling warnings through MRTR replay

**row:** `scaffold-warning-mrtr-typed-state` · **pri:** `Low` · **size:** `M` · **deps:** `scaffold-warning-domain-data`

## Anchors

- `src/RoslynMcp.Host.Stdio/ProtocolCompatibility/RequestStateCodec.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`

## Acceptance

- Preserve and restore the typed domain warning payload through suggestion request state; remove warning-prose parsing and duplicated warning text from the codec.
- Render public warning prose only at the terminal scaffolding boundary while preserving legacy workspace-only state and performing no second sibling scan.
- Reject malformed or unknown typed warning state fail closed.

## Regression

Round-trip one typed incomplete-discovery warning through MRTR replay, assert terminal rendering occurs exactly once, and table-test malformed, unknown, and legacy workspace-only state without rescanning siblings.

## Evidence

`ScaffoldingTools` owns both preservation and terminal restoration/rendering, while `RequestStateCodec` owns compound state. PR #1472's cold review confirmed these two production files form a separate dependent change from the domain-payload correction.
