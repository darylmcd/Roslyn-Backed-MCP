# tool-call-error-envelope-wire-contract — Lock the serialized tools/call error contract

**row:** `tool-call-error-envelope-wire-contract` · **pri:** `High` · **size:** `S` · **deps:** `tool-error-envelope-sensitive-detail-disclosure,protocol-version-result-shape-wire-contract`

## Anchors

- New `tests/RoslynMcp.Tests/ToolCallErrorWireContractTests.cs`.
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs`
- `tests/RoslynMcp.Tests/Helpers/InMemoryMcpClientServerHarness.cs`

## Acceptance

- [ ] Register one synthetic tool that throws an unexpected exception with nested secret sentinels and capture its raw JSON-RPC response.
- [ ] Assert a JSON-RPC `result` with `isError: true`, one parseable public application-error text block, and no JSON-RPC protocol `error`.
- [ ] Parse `content[0].text`: application `_meta` is present, `schemaHint` is absent for unexpected InternalError, and neither field is accidentally promoted to protocol result `_meta`.
- [ ] Legacy output omits `resultType`; the 2026-07-28 frame carries `resultType: complete`.
- [ ] Assert the secret sentinel, exception type, inner messages, stack frames, and local paths are absent.
- [ ] Parameterize only the supported protocol eras needed to prove the envelope has no SDK-version drift.

## Evidence

- Current in-process tests did not catch the shipped SDK response-construction regression or prove public redaction after serialization.
- The dirty wire suite covers successful calls only and does not assert error-envelope serialization.
