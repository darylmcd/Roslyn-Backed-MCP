---
category: Fixed
---

- **Fixed:** `add_package_reference_preview` now probes the evaluated MSBuild PackageReference graph via `IMsBuildEvaluationService` before building the diff — packages already contributed via `Directory.Build.props` / `Directory.Packages.props` / SDK imports are detected (not just raw `.csproj` scan) and the tool raises `"already present in the evaluated project graph"` instead of emitting a duplicate `<PackageReference>` NuGet will later refuse (`ProjectMutationService`). (`add-package-reference-preview-cpm-duplicate-detection`)
