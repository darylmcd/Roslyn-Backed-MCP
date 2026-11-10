# per-workspace-caches-publish-after-close — Close the publish-after-close window in manager-external per-workspace caches

**row:** `per-workspace-caches-publish-after-close` · **pri:** `Medium` · **size:** `M` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`
- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs`
- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs`
- `src/RoslynMcp.Roslyn/Services/NuGetDependencyService.cs`

## Acceptance

- [ ] Each cache either is session-owned or checks a per-workspace generation at publish time so a reader overlapping Close cannot leave an entry; TestDiscoveryService._cache is released on close and reload (this subsumes test-discovery-cache-session-lifetime).
- [ ] Audit DiagnosticQueryService (_diagnosticCache/_resultCache, publishes at ~:108/:250), CompilationCache insert path and PostApplySymbolResolver._hints (keyed by preview token, no close release) and fix or split each with file:line.
- [ ] Per-cache deterministic close-race test, mutation-verified (the guard removed makes it red).

## Evidence

- PR 2053 review: the project index was fixed by making it session-owned; these services live outside WorkspaceManager and share the same publish-after-invalidate shape (DiRegistrationService.cs:38/:163, TestDiscoveryService.cs:18, MsBuildEvaluationService.cs:29/:149 pinning ProjectCollection handles, NuGetDependencyService.cs:38/:242).
