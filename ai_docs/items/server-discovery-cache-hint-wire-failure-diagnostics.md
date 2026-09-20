# server-discovery-cache-hint-wire-failure-diagnostics — server-discovery-cache-hint-wire-failure-diagnostics

**row:** `server-discovery-cache-hint-wire-failure-diagnostics` · **pri:** `Low` · **size:** `S` · **deps:** `resource-cache-hint-wire-failure-diagnostics`

# server-discovery-cache-hint-wire-failure-diagnostics — diagnose cache-hint wire failures

## Anchors

- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs:535-550`

## Acceptance

- [ ] Cache-hint assertions use `TryGetProperty` before value access and include negotiated era plus the captured response frame on failure.
- [ ] Modern responses still require the exact cache-hint values and legacy responses still require omission.
- [ ] A focused regression proves a missing or malformed hint fails with the bounded diagnostic rather than a context-free `KeyNotFoundException`.

## Evidence

Cold deepening of `resource-cache-hint-wire-failure-diagnostics` found the same raw `GetProperty` diagnostic gap in the separate server-discovery wire suite. Keep it separate so each row owns one regression shape.
