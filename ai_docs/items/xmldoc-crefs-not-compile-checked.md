# xmldoc-crefs-not-compile-checked — dangling crefs cannot fail the build today

## Anchors

- `Directory.Build.props`
- `src/RoslynMcp.Roslyn/Services/EditService.cs`

## Acceptance

- [ ] `GenerateDocumentationFile` (or an explicit `DocumentationFile`) is enabled repo-wide with CS1591 suppressed, so CS1574/CS1580 dangling-cref warnings become errors under the existing `TreatWarningsAsErrors=true`.
- [ ] Any dangling crefs the flip surfaces across `src/**` are fixed (split per project if the count exceeds one PR's size cap); a deliberately broken cref fails the build.

## Evidence

Verified by inspection during the PR #1241 review, not hypothesized: grep for `GenerateDocumentationFile`/`DocumentationFile` across `Directory.Build.props` and every `src` csproj returns ZERO hits, so the C# compiler never validates crefs. That is exactly why that PR's `<see cref="ValidateEditRange"/>` — naming a symbol that does not exist — built green under `TreatWarningsAsErrors`.

This is the class fix; `editservice-dead-validateeditrange-references` is the instance.
