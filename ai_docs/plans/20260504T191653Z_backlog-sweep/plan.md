# Backlog sweep plan — 20260504T191653Z
<!-- purpose: Backlog sweep plan for the 20260504T191653Z initiative batch. -->
<!-- scope: in-repo -->

**Generated:** 2026-05-04T19:16:53Z
**Backlog snapshot:** 2026-05-03T14:20:21Z
**Initiative count:** 1
**Anchor verification:** performed

The current backlog has only one open row (Low priority) plus one explicitly Defer'ed row. Critical / High / Medium are empty. This plan covers the single eligible row.

## Initiatives (in order)

### 1. navigation-tools-misnamed-locator-error

| Field | Content |
|---|---|
| Status | pending |
| Backlog rows closed | `navigation-tools-misnamed-locator-error` |
| Diagnosis | PR #474 fixed `symbol_info` by introducing `SymbolLocatorFactory.FormatSymbolNotFoundMessage(SymbolLocator)` (helper at `src/RoslynMcp.Host.Stdio/Tools/SymbolLocatorFactory.cs:100`). Sibling navigation tools still throw the legacy literal `"No symbol found at the specified location"`. Live throw sites verified by grep: `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:258` (callers_callees), `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:308` (find_consumers), `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs:32`, `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:330`. Each site already has a `SymbolLocator` in scope (built one or two lines above the throw via `SymbolLocatorFactory.Create(...)`). Note: backlog row text cites four lines `232/258/308/358` in `AnalysisTools.cs` and names tools `symbol_relationships`, `goto_type_definition`, `find_consumers`. Live grep finds only 2 throws in `AnalysisTools.cs` (258/308); the other two legacy sites are in sibling files. The executor should treat the live grep as authoritative. |
| Approach | Replace the literal string at each of the 4 throw sites with `SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator)` so the message names the supplied locator field (`filePath:line:column`, `symbolHandle`, or `metadataName`). Pattern: `throw new KeyNotFoundException(SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator));`. Where the local variable is named differently, hoist or rename the locator into a local before the service call. No service-layer changes. |
| Scope | Production files: 3 — `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs` (2 throw sites), `src/RoslynMcp.Host.Stdio/Tools/ConsumerAnalysisTools.cs` (1 site), `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (1 site). Test files: 1 new — `tests/RoslynMcp.Tests/Services/NavigationToolsNotFoundMessageTests.cs` modeled on the existing `tests/RoslynMcp.Tests/Services/SymbolInfoNotFoundMessageTests.cs`. Tool-surface-only exemption does NOT apply (3 files); within the standard Rule 3 cap of 4 production files. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | (1) The 4 throw sites span 3 files — verify each call site has a `SymbolLocator` named `locator` (or hoist to one) before the throw. Two sites in `AnalysisTools.cs` build the locator inline as the second arg to the service call; those need a local var introduced. (2) Tests must construct each `SymbolLocator` mode (filePath+line+col, symbolHandle, metadataName) for at least one tool to assert the right message branch fires; the existing `SymbolInfoNotFoundMessageTests.cs` shows the test shape. (3) Hotspot risk: none — `AnalysisTools.cs` is not on the hotspot list. (4) Backlog row mentions tool name `symbol_relationships`, but live `AnalysisTools.cs:258` is `callers_callees` (GetCallersCalleesAsync) and `:308` covers `find_consumers`; the executor should ship the fix at the live throw sites regardless of which tool name appears in the backlog text. |
| Validation | (a) `mcp__roslyn__compile_check` after each edit. (b) New `NavigationToolsNotFoundMessageTests.cs` covering at minimum: callers_callees with each locator shape (3 cases), find_consumers with one locator shape, find_type_mutations / find_type_usages with one locator shape, symbol_relationships path. (c) `./eng/verify-release.ps1 -Configuration Release` for the CI gate before opening the PR. (d) Existing `SymbolInfoNotFoundMessageTests.cs` must continue to pass unchanged. |
| Performance review | N/A — error-message branch only fires on the not-found path; no hot-path code changed. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed: navigation tools `callers_callees`, `find_consumers`, `find_type_mutations`/`find_type_usages`, and the `SymbolTools` resolver now emit a locator-aware "no symbol found" message naming the field the caller supplied (`filePath:line:column`, `symbolHandle`, or `metadataName`), matching the fix shipped for `symbol_info` in PR #474. Closes `navigation-tools-misnamed-locator-error`. |
| Backlog sync | Close rows: [`navigation-tools-misnamed-locator-error`]. Mark obsolete: []. Update related: []. |

## Skipped rows

| Row | Reason |
|---|---|
| `workspace-process-pool-or-daemon` | Explicitly Defer'ed pending worse-profile evidence (per backlog Defer section). |

## Self-vet checklist

- [x] Rule 1: 1 row → 1 initiative. No bundling.
- [x] Rule 3: 3 production files (cap 4).
- [x] Rule 3b: `toolPolicy: edit-only` — diff is textual and local; no `*_apply` / `*_preview` involved.
- [x] Rule 4: 1 new test file (cap 3).
- [x] Rule 5: 30K estimate (ceiling 80K).
- [x] No hotspot files touched (`AnalysisTools.cs`, `ConsumerAnalysisTools.cs`, `SymbolTools.cs` are not on the addenda hotspot list).
- [x] Markdown link hrefs: all source citations use plain inline code; no markdown-link form pointing into `src/`. Verified safe against `verify-ai-docs.ps1`.
