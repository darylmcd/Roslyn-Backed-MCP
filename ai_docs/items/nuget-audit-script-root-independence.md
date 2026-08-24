# nuget-audit-script-root-independence — Resolve audit inputs independently of caller CWD

**row:** `nuget-audit-script-root-independence` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-nuget-audit.ps1`
- `tests/RoslynMcp.Tests/NuGetAuditGateTests.cs`

## Acceptance

- [ ] Resolve a relative `SolutionPath` against the repository root derived from `$PSScriptRoot`; preserve explicit absolute paths.
- [ ] Fail clearly when the resolved solution is absent and never restore a same-named solution from the caller's current directory.
- [ ] Add one process-level regression that invokes the script from an unrelated working directory and proves the repository-owned solution path reaches the `dotnet` boundary.
- [ ] Preserve fail-closed NU1900-NU1904 handling and the existing CI/Justfile invocation contract.

## Evidence

During PR #1326 review, invoking the script path from a disposable clone while the caller CWD remained the primary checkout restored the primary checkout instead. The script defaults `SolutionPath` to the relative string `RoslynMcp.slnx` and passes it directly to `dotnet restore`, so its audit target is silently coupled to caller CWD.
