# tool-consolidation-adr-and-alias-machinery — ADR + deprecated-alias machinery for the tool consolidation

**row:** `tool-consolidation-adr-and-alias-machinery` · **pri:** `Medium` · **size:** `M`

## Anchors

- `docs/decisions/0003-tool-surface-policy.md` (new ADR)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ToolAliasDeprecation.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`

## Acceptance

- [ ] An ADR records the consolidation policy: merges stay WITHIN risk buckets (formatting / text-edit / code-transform / file-lifecycle / project-file), old names survive as declared deprecated aliases for at least one minor cycle, and removal happens only at the next major.
- [ ] The declared-deprecated-alias mechanism is proven end to end for one representative merge — old name still resolves, is advertised as deprecated, and the catalog test asserts alias parity.
- [ ] A migration note exists for the published surface (Directive #4).

## Evidence

The audit found 18 disjoint merge groups, max merge 173 -> ~113; the risk-aligned variant gives up only 3-4 tools of reduction while keeping the safety surface — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §2. Structural prerequisites already shipped: every `*_apply` takes only previewToken+workspaceId, and `symbol_refactor_preview` ships the kind-discriminated pattern.

## Context

Split from `risk-aligned-tool-consolidation` (2026-09-02). This is the unblocking child: the two execution children carry a `deps` edge onto it.

**This is a published-surface contract change** (Directive #4 — `roslyn-mcp@roslyn-mcp-marketplace`), so the policy and the alias machinery must exist and be proven before any merge ships. Plan under ADR 0003 and the current release policy.

**Fold in:** `apply-composite-preview-destructive-misnomer`'s rename belongs in the same deprecation cycle rather than as a separate contract break.

**Rejected and recorded:** dynamic toolsets — `list_changed` is unreliable across clients and GitHub removed theirs 2026-05-20 (report §6). **Related-not-duplicate:** `tool-surface-pagination-or-tool-sets` (catalog resources, no hiding).
