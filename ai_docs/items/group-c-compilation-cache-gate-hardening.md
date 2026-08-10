# group-c-compilation-cache-gate-hardening — harden the opt-in cache gate before any production consumer is wired

**row:** `group-c-compilation-cache-gate-hardening` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:51` (`CanUseCompilationCache`)
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:175` (gate evaluated once before the project loop)
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:353` (same, second call site)
- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs:213` (same)
- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:72` (re-reads `GetCurrentVersion` per call)

## Acceptance

- [ ] A mid-scan workspace version bump can no longer serve a stale snapshot: either the liveness gate is re-checked per project iteration, or the version observed at gate time is pinned and re-verified at each cache fetch. A regression test bumps the workspace version between two project iterations and asserts the loop falls back to `project.GetCompilationAsync`.
- [ ] A caller supplying `compilationCache` without `workspaceId` (or with a blank one) fails loudly with `ArgumentException`, or the two are bundled into a single optional parameter so they cannot be half-supplied. Today that combination silently disables caching with no diagnostic.
- [ ] The three copies of the `useCache ? compilationCache!.GetCompilationAsync(...) : project.GetCompilationAsync(...)` dispatch collapse into one shared internal fetch helper, so the null-forgiving `!` operators have a single guard to depend on.

## Evidence

- Traced during the code-quality review of PR #1207 (`compilation-cache-adoption-read-side`): the gate result is computed once before `foreach (var project in solution.Projects)` at `SymbolResolver.cs:175` / `:353` and `SymbolHandleSerializer.cs:213`, while `CompilationCache.GetCompilationAsync` re-reads `_workspaceManager.GetCurrentVersion(workspaceId)` on every call and keys only on `(workspaceId, ProjectId, version)`. A version bump between iterations therefore returns compilations keyed at the NEW version for `Project` instances drawn from the pre-bump snapshot solution.

## Context

PR #1207 routed the group-(c) helpers (`SymbolResolver.ResolveByMetadataNameAsync`, `SymbolResolver.FindClosestMatchesAsync`, `SymbolHandleSerializer`) through `ICompilationCache` behind **optional** parameters — `ICompilationCache? compilationCache = null, string? workspaceId = null` — so that every existing caller compiles unchanged and takes the raw path. That shape was chosen deliberately: these are `static` helpers whose only state is the `Solution` parameter, and threading a REQUIRED cache parameter would have touched 6+ production files (`ChangeSignatureService`, `CrossProjectRefactoringService`, `SymbolHandleSerializer`, `RecordFieldAdditionService`, `SymbolRefactorService`, `SymbolRelationshipService`), blowing Rule 3's 4-file cap.

The consequence is that **no production caller opts in yet**, which is why all three findings here are pre-consumer hardening rather than live bugs: the version-bump race is unreachable today. Fix it before the first consumer is wired, not after — the whole point of the liveness gate is to prevent exactly the live-vs-caller mismatch this race reintroduces by a different route (a race instead of a fork).

## Notes

- The forked-solution case this gate exists for is real and confirmed live: `CrossProjectRefactoringService.cs:236` calls `ResolveByMetadataNameAsync(updatedSolution, ...)` with a forked solution and must never be opted in.
- Confirmed forked exclusions that must stay on the raw path: `InterfaceExtractionService:515`, `RefactoringService:415`, `TypeMoveService:217`.
