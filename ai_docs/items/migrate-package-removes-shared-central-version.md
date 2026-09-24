# migrate-package-removes-shared-central-version — Keep shared central PackageVersion entries when projects outside the workspace consume them

**row:** `migrate-package-removes-shared-central-version` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PackageMigrationOrchestrator.cs:164`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:517`

## Acceptance

- [ ] migrate_package_preview(MSTest.TestAdapter→X) on the sample workspace keeps the central entry (or emits a warning naming tests/RoslynMcp.Tests)
- [ ] remove_central_package_version_preview same
- [ ] Regression test with an out-of-workspace consumer

## Evidence

- migrate_package_preview diff removes <PackageVersion Include="MSTest.TestAdapter"> from <repo>/Directory.Packages.props although tests/RoslynMcp.Tests and 2 more csproj reference it; warnings:null. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
