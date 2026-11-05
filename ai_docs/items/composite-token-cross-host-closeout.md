# composite-token-cross-host-closeout — Close the portable composite token contract

**row:** `composite-token-cross-host-closeout` · **pri:** `Medium` · **size:** `S` · **deps:** `composite-preview-source-fingerprint-refusal, composite-redemption-admission-boundary`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/ProductionParityMcpHarness.cs`
- `docs/release-policy.md`
- `ai_docs/runtime.md`
- `ai_docs/references/environment-variables.md`

## Acceptance

- [ ] `tools/call apply_composite` through two MCP harnesses succeeds cross-host.
- [ ] Docs state persisted-token portability; the held lifecycle boundary is re-vetted on the new base.

## Evidence

- `docs/release-policy.md:19` contradicts the persistence feature.
