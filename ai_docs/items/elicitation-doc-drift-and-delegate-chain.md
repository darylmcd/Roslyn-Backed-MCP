# elicitation-doc-drift-and-delegate-chain — single-source the TryElicitChoiceAsync docs and collapse the 3-hop forwarders

**row:** `elicitation-doc-drift-and-delegate-chain` · **pri:** `Low` · **size:** `M` · **deps:** `elicitation-trychoice-cancellation-swallow`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs:14-15` (class summary claims ownership it no longer has)
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs:262-274` (duplicated param/returns doc block)
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:472-485` (third copy of the same block)
- `src/RoslynMcp.Host.Stdio/Middleware/ElicitationAllowlistPolicy.cs:68` (middle hop)
- `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs:19,49,58-72,74` (canonical home)

## Acceptance

- [ ] `StructuredCallElicitationCoordinator`'s class summary no longer claims to own `TryElicitChoiceAsync`; it names `ElicitationChoicePrompt` as the canonical home.
- [ ] The ~13-line param/returns doc block for `TryElicitChoiceAsync` exists exactly ONCE (on `ElicitationChoicePrompt`); both forwarding delegates use `<inheritdoc cref="ElicitationChoicePrompt.TryElicitChoiceAsync"/>`, matching the existing thin-delegate shape at `StructuredCallToolFilter.cs:229-249`.
- [ ] `HasElicitation` and `TryElicitChoiceAsync` have exactly one definition each. The four elicitation test suites call `ElicitationChoicePrompt` directly and the intermediate forwarders in `StructuredCallToolFilter` / `ElicitationAllowlistPolicy` / `StructuredCallElicitationCoordinator` are deleted; the Middleware↔Tools cycle guard test still passes.
- [ ] The layering-invariant doc at `ElicitationChoicePrompt.cs:19` stops hardcoding the guard test's fully-qualified method name in prose (reference the test file or this row id instead, so a rename cannot silently rot it).

## Evidence

- Traced during the code-quality review of PR #1205 (`hoststdio-middleware-tools-namespace-cycle`): the param/returns block is byte-identical in three files, and the coordinator's class summary still asserts ownership of a body that moved. Every production call site (`SymbolTools.cs:93,105,933,941`) already targets `ElicitationChoicePrompt` directly, so the two middle hops carry no production traffic — the remaining references are exclusively in tests (`StructuredCallToolFilterElicitationTests.cs:59,127,141`; `SymbolDisambiguationElicitationTests.cs:80,84,120,123`; `StructuredCallElicitationCoordinatorTests.cs:189`; `ElicitationAllowlistPolicyTests.cs:29,37,46`).

## Context

PR #1205 broke the `RoslynMcp.Host.Stdio.Middleware` ↔ `RoslynMcp.Host.Stdio.Tools` namespace cycle by extracting the elicitation choice picker into a new `RoslynMcp.Host.Stdio.Elicitation` namespace. The extraction was deliberately additive — it left forwarding delegates behind so the four existing test suites kept compiling — which is why this cleanup is a follow-on rather than part of that PR.

Consolidated from two separate reviewer sketches (doc drift; 3-hop delegate chain) because both touch the same four files and would collide as separate PRs.

## Notes

- All members involved are `internal`, so deleting the forwarders is not a published-surface change (Directive #4 does not apply).
- Do NOT widen production visibility to suit tests; `InternalsVisibleTo RoslynMcp.Tests` already exists at `src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj:43` and the Host.Stdio project has the equivalent.
- Related but separate: the bare-catch cancellation swallow in the same file is tracked by `elicitation-trychoice-cancellation-swallow`. Sequence that row FIRST if both are picked up, since it edits the same method body.
Post elicitation-trychoice-cancellation-swallow (landed): the <returns> doc block this row consolidates now additionally says 'null on decline / cancel / unsupported / error' which is factually wrong — cancel no longer returns null, it propagates OperationCanceledException. When consolidating to the single canonical copy on ElicitationChoicePrompt, also correct the text (only InvalidOperationException/McpException degrade to null; OperationCanceledException propagates).
