# evaluated-central-package-policy — Evaluate active central package policy and versions

**row:** `evaluated-central-package-policy` · **pri:** `Medium` · **size:** `M` · **deps:** `msbuild-evaluation-workspace-configuration`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs`: IsCentralPackageManagementEnabled, ContainsCentralPackageVersion, TryGetCentralPackageVersion.
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs`: GetNuGetDependenciesDetailedAsync central-version resolution.
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs`: PreviewAddPackageReferenceAsync and central-version previews.
- `src/RoslynMcp.Roslyn/Services/PackageMigrationOrchestrator.cs`: PreviewMigratePackageAsync central-policy selection.
- `tests/RoslynMcp.Tests/NuGetDependencySummaryTests.cs`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs`

## Acceptance

- [ ] Derive effective CPM policy and active package versions from evaluated imports/items/properties under the captured workspace configuration and target identity.
- [ ] Preserve explicit XML inspection helpers only for syntax contracts; semantic inspection and mutation decisions must use evaluated authority.
- [ ] Bind edits to the actual owning authored declaration; preserve imported/conditional policy and refuse concretely unrepresentable edits before effects.
- [ ] Observe original conditional/imported CPM and expanded/version-override failures; verify selected targets and separately configured sessions remain isolated.
- [ ] Preserve cancellation and safe incomplete-result reporting; captured property values must not enter public failures or logs.

## Evidence

- Source-only inspection 2026-10-15: NuGetDependencyService reads effective PackageReference items but resolves central versions through nearest raw Directory.Packages.props XML.
- ProjectMutationService and PackageMigrationOrchestrator infer central-management state from raw first matching XML values; conditions, property expansion and imported policy can disagree with the active build.
- Distinct from restoring captured assets/package roots; these inspection/mutation consumers do not call the corrected evaluated restore-input path. No runtime proof or completion claim.
