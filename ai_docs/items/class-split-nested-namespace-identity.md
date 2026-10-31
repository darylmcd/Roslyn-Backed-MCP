# class-split-nested-namespace-identity — Preserve the full namespace chain

**row:** `class-split-nested-namespace-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:121`
- `tests/RoslynMcp.Tests/ClassSplitReceivingNamespaceIntegrationTests.cs` (extend)

## Acceptance

- [ ] Split a class under nested namespace declarations through public preview/apply; the generated partial retains the full namespace identity.
- [ ] Compile and bind the serialized preview and applied/reloaded solution; preserve unrelated source bytes.

## Evidence

- Cold implementation review on 2026-10-31: `GetNamespaceName` returns only the nearest `BaseNamespaceDeclarationSyntax.Name`, so `Outer.Inner.Worker` is emitted in `Inner`.
