# input-examples-feasibility — Probe whether input_examples are expressible for MCP-served tools

**row:** `input-examples-feasibility` · **pri:** `Low` · **size:** `S`

## Anchors

- Investigation note (outcome may be a wont-do close, not code)
- `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs` (pilot: change_signature_preview)
- `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs` (pilot: parameter_object_preview)
- `src/RoslynMcp.Host.Stdio/Tools/SymbolRefactorTools.cs` (pilot: symbol_refactor_preview)

## Acceptance

- [ ] Determine whether Anthropic `input_examples` reach clients from an MCP server (tool `_meta`? SDK 2.x surface? client-side only?) — with a wire-probe or doc citation as proof
- [ ] If expressible: pilot on the 3 named complex tools (~100–200 tokens each) and measure schema cost
- [ ] If not expressible: close the row wont-do with the evidence recorded here

## Evidence

- Anthropic measured parameter-accuracy 72%→90% with input_examples; MCP expressibility unconfirmed in SDK release notes — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §5
