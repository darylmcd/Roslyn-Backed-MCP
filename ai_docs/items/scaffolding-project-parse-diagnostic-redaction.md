# scaffolding-project-parse-diagnostic-redaction — Sanitize project-parse fallbacks

**row:** `scaffolding-project-parse-diagnostic-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,scaffolding-io-warning-detail-redaction`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScaffoldingService.cs`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs`

## Acceptance

- [ ] Framework-detection and test-project-validation parse fallbacks preserve deterministic `mstest`/allow-through behavior without logging raw exception objects, full project paths, or secret-bearing values.
- [ ] Both branches use the shared secret-safe diagnostic projection and request correlation.
- [ ] Invert the existing malformed-project tests to prove the enabled sink retains benign exception topology while logs and diagnostics exclude the nested sentinel.
- [ ] Successful project parsing and validation remain unchanged.

## Evidence

- `ScaffoldingService` attaches the raw parse exception and absolute project path to two warning logs; the existing tests explicitly require both disclosures.
- Found while editing the adjacent scaffolding IO-fallback path on 2026-08-17.
