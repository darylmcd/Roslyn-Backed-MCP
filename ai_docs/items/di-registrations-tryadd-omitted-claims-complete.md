# di-registrations-tryadd-omitted-claims-complete — Include TryAdd* registrations (or report partial) in get_di_registrations

**row:** `di-registrations-tryadd-omitted-claims-complete` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:181`

## Acceptance

- [ ] Host.Stdio count matches source (17 incl. TryAddSingleton at ServiceCollectionExtensions.cs:80-82) or totalCountMeaning != complete
- [ ] Schema text mentions the behavior

## Evidence

- get_di_registrations(Host.Stdio) → 14, totalCountMeaning 'complete'; source has 17. — see `ai_docs/audits/20260924-1305/report.md` (check C2) and `ai_docs/audits/20260924-1305/findings.json`
