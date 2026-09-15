# code-fix-provider-cache-reference-lifetime

**row:** `code-fix-provider-cache-reference-lifetime` · **pri:** `Low` · **size:** `S`

## Anchors

- src/RoslynMcp.Roslyn/Services/CodeFixProviderRegistry.cs
- tests/RoslynMcp.Tests/CodeFixProviderRegistryTests.cs

## Acceptance

- [ ] Cache analyzer providers by reference/load-context identity so a reloaded reference at the same path cannot reuse providers from a retired context.
- [ ] Use platform-correct path identity when deduplicating shared references; distinct case-sensitive paths must not collapse.
- [ ] Prove a same-path replacement reference uses its own loader while repeated queries for the same reference reuse providers.

## Evidence

The process-lived registry caches strong provider instances solely by case-insensitive path; GetAssembly now respects reference-owned isolation, but an earlier reference at that path can still supply the cached providers. Observed during 2026-09-15 diagnostic provider remediation.
