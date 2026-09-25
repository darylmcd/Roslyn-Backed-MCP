# change-signature-callsite-rewrites-enclosing-invocation — Rewrite only real invocations in change_signature_preview

**row:** `change-signature-callsite-rewrites-enclosing-invocation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:134`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] op=add/remove on a method referenced as `list.Select(svc.Compute)` or `nameof(svc.Compute)` leaves the enclosing `Select` / `nameof` call unchanged and does not count it in `callsiteUpdates`.
- [ ] Regression test covers a method-group reference and a nameof reference next to a normal invocation.

## Evidence

- Traced by code read at HEAD (not reproduced): `ChangeSignatureAddRemovePreviewBuilder.cs:134` `var invocation = oldRoot.FindNode(span).FirstAncestorOrSelf<InvocationExpressionSyntax>();` runs on every `SymbolFinder.FindCallersAsync` location, so a non-invocation reference walks up to the nearest enclosing invocation and hands that call's argument list to `updateCallsite`. Pre-existing; surfaced by the cold review of PR #1617.

## Context

Spin-off from `change-signature-add-skips-same-document-impls` (PR #1617), which only moved this line.
