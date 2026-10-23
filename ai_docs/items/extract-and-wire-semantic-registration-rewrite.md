# extract-and-wire-semantic-registration-rewrite — Rewrite only bound DI registrations

**row:** `extract-and-wire-semantic-registration-rewrite` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:188`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs`

## Acceptance

- Bind supported DI registration invocations and selected implementation symbols, then rewrite syntax type arguments with the generated interface identity.
- Preserve comments, literals, unrelated same-name types and existing whitespace; cover qualified and spaced generic registrations with compilation checks.
- Keep supported lifetime forms; do not substitute another text-matching heuristic.

## Evidence

- Static review2026-10-17: RewriteDiRegistration uses ordinal String.Replace for AddSingleton, AddScoped and AddTransient with a simple type-name token.
- The same bytes in comments, literals or unrelated methods are also replaced, while whitespace and qualified spelling can prevent intended replacements.
- Separate semantic invocation-selection mechanism from generated namespace binding and same-file mutation composition. No runtime reproduction or fix is claimed; not selected for implementation.
