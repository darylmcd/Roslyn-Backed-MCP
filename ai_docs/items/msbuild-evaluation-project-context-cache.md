# msbuild-evaluation-project-context-cache — Bind MSBuild query cache to actual project context

**row:** `msbuild-evaluation-project-context-cache` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:29` — cache key; GetOrLoadProject at143–163 creates an unconfigured ProjectCollection and loads only FilePath.
- `tests/RoslynMcp.Tests/MsBuildEvaluationServiceTests.cs:17` — query and invalidation controls.

## Acceptance

- [ ] Observe RED for explicit Release/custom workspace globals and one physical multi-target project whose real property/item results differ by selected loaded context; cover all three query APIs.
- [ ] Bind native evaluation and cache identity to the same actual loaded context. Preserve workspace reload/close invalidation, collection ownership/disposal, cancellation and concurrent access.
- [ ] Preserve each tool and service consumer; do not infer configuration from project names, output paths or source symbols. Re-derive the integration seam and size before implementation.
- [ ] Pass independent review, affected tests and hosted gates before closure.

## Evidence

- Current main58ba4207 source inspection: inner cache key is version plus filePath; workspace id alone does not distinguish multiple target-framework projects sharing a physical file. Native LoadProject receives no loaded globals.
- Semantic consumers include MSBuildTools, NuGetDependencyService, ProjectMutationService and SecurityDiagnosticService; no caller rewrite is assumed necessary.
- No causal runtime regression has been executed for this service; source-level finding only. Existing restore-metadata-evaluation-global-context owns the different helper/parser/staleness seam.
- Intake: `<user>/.codex/scratch/roslyn-remediate-20261023-split/restore-closeout-independent-intake.md`.

## Context

- Independent nonblocking follow-up; no dependency from restore-callers-missing-packages-path.
