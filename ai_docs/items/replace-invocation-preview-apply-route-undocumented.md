# replace-invocation-preview-apply-route-undocumented — document and map replace_invocation_preview's shared apply route

**row:** `replace-invocation-preview-apply-route-undocumented` · **pri:** `Medium` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/BulkRefactoringTools.cs` (the `bulk_replace_type_apply` `previewToken` description, and `replace_invocation_preview`'s description)
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs` (`PreviewApplyRoutes`)

## Acceptance

- [ ] `bulk_replace_type_apply`'s `previewToken` description names BOTH accepted producers (`bulk_replace_type_preview` and `replace_invocation_preview`), and `replace_invocation_preview`'s description names its apply route.
- [ ] `ServerSurfaceCatalog.PreviewApplyRoutes` maps `replace_invocation_preview` to `bulk_replace_type_apply`, so `SelectTools`' preview/apply closure covers it.

## Evidence

Cold code-quality review of PR #1384. Two producers now share `PreviewKind.BulkReplaceType`, which makes the pairing an ENFORCED runtime contract — but the wire-visible MCP schema still describes only one producer, so a caller holding a `replace_invocation_preview` token has nothing telling it where that token redeems.

Separately, `TryGetCompatibleApplyRoute("replace_invocation_preview")` returns false against the live dictionary, so `SelectTools` — which documents that it "keeps preview issuance closed over its compatible apply routes" — skips it entirely. Latent only because both tools sit in the `experimental` tier today and therefore always co-select; a tier divergence would surface a preview tool whose apply route was filtered out.

The dictionary is already many-to-one (nine `*_preview` keys point at `apply_project_mutation`), so the mapping is a one-line addition.

## Context

Related to `catalog-preview-apply-pairing-pin-all-tiers`, which would add the test that catches this class; this row is the data fix plus the schema wording.
