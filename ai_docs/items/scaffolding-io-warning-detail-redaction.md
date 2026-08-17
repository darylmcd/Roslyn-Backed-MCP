# scaffolding-io-warning-detail-redaction — Sanitize scaffolding IO fallback warnings

**row:** `scaffolding-io-warning-detail-redaction` · **pri:** `High` · **size:** `M` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Reference-test and sibling-fixture read failures retain the affected operation and deterministic fallback action without exception type/raw message/full path/secret sentinel.
- [ ] Server diagnostics use the shared secret-safe diagnostic projection and request correlation.
- [ ] Successful pattern inference and generated test content remain byte-equivalent.
- [ ] One table-driven single/batch IO-failure test proves both client warnings use the shared policy.
- [ ] Keep sampling-provider fallback owned by `mcp-sampling-mrtr-migration`.

## Evidence

- Both scaffolders catch IO/authorization exceptions and embed `ex.GetType().Name` plus `ex.Message` in successful client warning DTOs.
- This path is code-traced; add raw tool-response coverage during implementation.
