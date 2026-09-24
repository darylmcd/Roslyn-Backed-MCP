# di-registrations-factory-lambda-impl-misattributed — Resolve factory-lambda implementation types from the created object

**row:** `di-registrations-factory-lambda-impl-misattributed` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:535`

## Acceptance

- [ ] AddSingleton(sp => new NuGetVersionChecker(sp.GetRequiredService<IHttpClientFactory>(), …)) reports NuGetVersionChecker
- [ ] Doc comment at :525 corrected
- [ ] Regression test covers ctor-arg and forward (sp => sp.GetRequiredService<T>()) shapes

## Evidence

- ServiceCollectionExtensions.cs:87 → implementationType 'System.Net.Http.IHttpClientFactory'; :97 → 'ILogger<WorkspaceCacheStore>'. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
