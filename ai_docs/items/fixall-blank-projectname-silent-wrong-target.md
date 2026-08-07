# fixall-blank-projectname-silent-wrong-target — fix_all project scope must reject a blank projectName instead of silently targeting the first project

**row:** `fixall-blank-projectname-silent-wrong-target` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:233-237`
- `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs:29`

## Acceptance

- [ ] `FixAllService.ResolveTargets` throws an `ArgumentException` naming the required `projectName` parameter when `scope == "project"` and `projectName` is null or whitespace, mirroring `MsBuildEvaluationService.ResolveRoslynProject:203`'s existing guard.
- [ ] A test asserts `fix_all` with `scope: "project"` and a blank/omitted `projectName` surfaces the corrective error rather than previewing fixes against `solution.Projects.First()`.

## Evidence

- Traced through the call chain during code-quality review of PR #1191 (`project-filter-helper-whitespace-normalize`): `FixAllTools.cs:29` declares `projectName` optional; `FixAllService.cs:235-237` calls `FilterProjects` then `FirstOrDefault`. `ProjectFilterHelper`'s new blank-means-no-filter semantics (shipped in PR #1191) makes a whitespace `projectName` return the whole project list, so the `InvalidOperationException` at line 237 becomes unreachable for that input — `fix_all` silently targets the first project in the solution instead of failing loud. `MsBuildEvaluationService.cs:203` already guards this way and is unaffected.

## Context

Spin-off from PR #1191, which fixed `ProjectFilterHelper`'s whitespace handling for the read-side (`compile_check`) but exposed this pre-existing gap in `FixAllService`'s single-project resolution — a behavior change riding on the helper's semantics shift, not touched by that PR's declared 2-file scope.
