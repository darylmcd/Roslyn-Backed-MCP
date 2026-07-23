---
category: Maintenance
---
Extracted the duplicated `Microsoft.CodeAnalysis.CSharp.Features` assembly-load-with-swallowed-exception logic from `CodeActionService` and `FixAllService` into a shared `CSharpFeaturesAssemblyLoader` helper (`src/RoslynMcp.Roslyn/Helpers/`), removing byte-for-byte-identical duplicate methods. No behavior change.
