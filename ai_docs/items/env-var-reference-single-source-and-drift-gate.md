# env-var-reference-single-source-and-drift-gate — Single-source the ROSLYNMCP_* reference and gate drift

**row:** `env-var-reference-single-source-and-drift-gate` · **pri:** `Medium` · **size:** `M`


## Anchors

- `ai_docs/references/environment-variables.md`
- `docs/setup.md`
- `src/RoslynMcp.Host.Stdio/README.md`
- `src/RoslynMcp.Host.Stdio/Program.cs`

## Acceptance

- [ ] A test fails when a ROSLYNMCP_* variable read in src/ is missing from the canonical doc.
- [ ] The other docs link to the canonical table instead of restating it.

## Evidence

wb: variable set 36=36 in environment-variables.md; wc1: setup.md covers 13 of 36. (doc-audit 2026-11-17)
