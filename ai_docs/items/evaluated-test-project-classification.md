# evaluated-test-project-classification — Classify test projects from actual evaluated context

**row:** `evaluated-test-project-classification` · **pri:** `Medium` · **size:** `M` · **deps:** `msbuild-evaluation-project-context-cache`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:172`
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:205`
- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:143`
- `ai_docs/items/msbuild-evaluation-project-context-cache.md`

## Acceptance

- [ ] Use actual loaded project context to classify effective IsTestProject and applicable test-framework package references, reusing the context-bound evaluation/cache seam rather than introducing a separate evaluator or the retained complete native graph.
- [ ] Observe old-red/new-green for imported IsTestProject=true, conditioned-out/false declarations and package references, differing actual globals and target frameworks sharing one physical project. Preserve ordinary known framework positives.
- [ ] Specify and test ad-hoc and missing/unreadable project behavior honestly; keep XML-only helpers for explicit syntax inspection if needed, not evaluated semantic decisions. Inventory every current IsTestProject(Project) consumer and preserve cancellation and evaluation lifetime ownership.
- [ ] Preserve the distinction from repeated-fullgraph performance: that retained graph capture mechanism is absent on main, and no current performance regression is asserted.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. INVESTIGATION/source-derived semantic defect: IsTestProject(Project)205-206 loads authored XML only; the XML overload selects the first IsTestProject and scans every PackageReference without evaluating conditions/imports. Existing context evaluation row remains necessary because its current service cache/evaluator does not yet bind actual loaded globals/TFM.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
