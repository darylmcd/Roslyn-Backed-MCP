# test-discovery-project-failure-silently-partial — Surface per-project discovery failures in test_discover results

**row:** `test-discovery-project-failure-silently-partial` · **pri:** `Medium` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs`

## Acceptance

- [ ] A project whose discovery throws (non-cancellation) is logged with the exception and reported through an existing payload-safe diagnostics channel; remaining projects still return; cancellation propagates; deterministic test with an injected failure.

## Evidence

- TestDiscoveryService.cs ~:79-82 logs and skips a failing project; same observability defect class as PR 2052 (sweep failures).
