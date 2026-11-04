# recursive-delete-no-follow-remaining-sites — Route the remaining recursive directory deletes through NoFollowDirectoryDeleter

**row:** `recursive-delete-no-follow-remaining-sites` · **pri:** `Medium` · **size:** `M` · **deps:** `analyzer-shadow-lease-disposal-no-follow`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:45`
- `src/RoslynMcp.Roslyn/Services/ForkAdmissionMaterial.cs:227`
- `src/RoslynMcp.Roslyn/Services/ForkAdmissionMaterial.cs:287`
- `src/RoslynMcp.Host.Stdio/Tools/TestCoverageTools.cs:281`

## Acceptance

- [ ] Each site deletes through NoFollowDirectoryDeleter; failures are surfaced, not swallowed (ForkAdmissionMaterial 227 and 287 are bare deletes inside try/catch-swallow).
- [ ] One red-first regression per site family: a descendant junction to an external directory leaves the target intact and the owned root is removed.
- [ ] No remaining Directory.Delete(*, true) under src/ (grep evidence in the PR).

## Evidence

- Listed by the PR 1964 author and confirmed by its cold reviewer: exactly these four sites remain. Same mechanism as analyzer-shadow-lease-disposal-no-follow.
