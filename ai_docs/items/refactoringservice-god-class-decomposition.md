# refactoringservice-god-class-decomposition — Decompose the 1555-line RefactoringService god-class

**row:** `refactoringservice-god-class-decomposition` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:24`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:936`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1329`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:763`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1112`

## Acceptance

- [ ] RefactoringService no longer contains a private GetOrCreateItemGroup duplicate; it calls the shared OrchestrationMsBuildXml helper.
- [ ] Document-set persistence logic is moved out of RefactoringService into a separate persistence type reused by EditService/ProjectMutationService where applicable.
- [ ] RebaseModifiedSolutionOntoCurrentAsync uses a precomputed FilePath index instead of a per-document linear SelectMany scan.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03a-roslyn-refactor-services::DG1-design, S03a-roslyn-refactor-services::DG7-config-deps-ergo, S03a-roslyn-refactor-services::DG2-cleanliness, S03a-roslyn-refactor-services::DG4-performance, S03a-roslyn-refactor-services::DG5-security-data
