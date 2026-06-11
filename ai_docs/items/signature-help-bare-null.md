# signature-help-bare-null — unresolvable locator returns bare JSON null instead of NotFound envelope

**row:** `signature-help-bare-null` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (`GetSignatureHelp`, ~line 537)

## Acceptance

- [ ] Mirror the member_hierarchy fix exactly — `if (result is null) throw new KeyNotFoundException(SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator));` before the serialize
- [ ] Regression: tool called with an unresolvable locator asserts a `KeyNotFoundException`/NotFound envelope, not bare `null` (mirror `NavigationToolsNotFoundMessageTests`)

## Evidence

- Discovered during the `member-hierarchy-bare-null` fix, 2026-06-05 top-5 remediation.

## Context

Sibling of `member-hierarchy-bare-null` (SHIPPED). `symbol_signature_help` (`SymbolTools.GetSignatureHelp`) serializes the service's nullable `GetSignatureHelpAsync` result directly, so an unresolvable locator returns a bare JSON `null` instead of the standard `{error, category:NotFound, message}` envelope — same ambiguity the member_hierarchy fix removed. (`symbol_relationships` is NOT affected — its tool layer already throws.)
