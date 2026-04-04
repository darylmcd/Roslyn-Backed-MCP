# Next work and backlog

## Instructions

This file is the current-state repository for unfinished work only.

- Add open, deferred, or follow-up work here when it remains actionable after the current task.
- Do not leave open work scattered across audit docs, plans, or inline comments.
- Remove items when they are completed and verified.
- Keep entries scoped, current, and linkable to the relevant audit or reference doc.
- Do not use this file as a changelog; completed-history narrative belongs in commit history or the archived deep-audit docs.

Last audit: 2026-04-04.

---

## P4 — Documentation, cosmetic, features, and release

- [ ] **DOC-01**: Prompt appendix resource/tool count drift — `ai_docs/prompts/deep-review-and-refactor.md` lists 6 resources; live catalog reports 7 stable (includes `roslyn://server/resource-templates`). Confirmed across all 4 audit repos. Reconcile on each release.
- [ ] **AUDIT-06**: `server_info` surface counts (116/6/16) don't match catalog counts. Minor discrepancy; subsumes prompt-vs-catalog count mismatch.
- [ ] **AUDIT-48**: `workspace_load` accepts `.slnx` files successfully but documentation/descriptor only mentions `.sln`/`.csproj`. On FirewallAnalyzer.slnx. Update docs to reflect actual support.
- [ ] **AUDIT-49**: `list_analyzers` field definitions unclear — `analyzerCount` vs `returnedAnalyzerCount` vs `totalRules` easy to misread. Pagination (`hasMore`) not obvious to single-shot callers. Confirmed on Roslyn-Backed-MCP.sln and ITChatBot.sln. Clarify field semantics in tool description.
- [ ] **FEAT-06**: NuGet vulnerability scanning — scan workspace package references for known CVEs via `dotnet list package --vulnerable --format json`. Extends `IDependencyAnalysisService` with a `nuget_vulnerability_scan` tool. Addresses OWASP A06:2021 (Vulnerable and Outdated Components). See `ai_docs/prompts/add-nuget-vulnerability-surface.prompt.md` for full specification.
- [ ] **CODE-01**: Reduce cyclomatic complexity in `RoslynMcp.Roslyn.Services` — prioritize methods that change often or are hard to test (e.g., high-CC paths such as `FindUnusedSymbolsAsync`, `ClassifyTypeUsage`, `GetDiRegistrationsAsync`, `DiscoverTestsAsync`, and long preview-style methods). Incremental extract-method / decomposition.
- [ ] **FEAT-02**: `.editorconfig` write support — current `get_editorconfig_options` is read-only. Add ability to modify style rules programmatically.
- [ ] **FEAT-03**: MSBuild property/item evaluation — query resolved MSBuild properties, conditional values, SDK metadata.
- [ ] **FEAT-04**: Suppression and severity management — programmatic pragma suppression, SuppressMessage, and .editorconfig severity overrides.
- [ ] **FEAT-05**: Maintainability Index — composite metric extending existing `complexity_metrics`.
- [ ] **CODE-02**: Cohesion / LCOM4 — exclude or down-rank test projects when using LCOM4 for production SRP decisions (tooling filter, defaults, or documented workflow). Test types such as `IntegrationTests` can show high LCOM4 legitimately.
- [ ] **REL-10**: Create 512x512 PNG icon for MCP directory listing and manifest. Required by [Anthropic MCP submission guide](https://support.claude.com/en/articles/12922832-local-mcp-server-submission-guide).
- [ ] **WORK-01**: If parallel test hosts cause MSBuild file-lock errors (`testhost.exe` holding outputs), run a full `dotnet test` on the solution after closing other hosts.

---

## Rules

- Keep only open/incomplete items. Remove items when resolved.
- Reprioritize on each audit pass.
- Bug and tool items: full reproduction and session context live in reports under `ai_docs/reports/` where applicable.
- Broader code review and complexity notes: `ai_docs/archive/deep-review-report.md` and consolidated items in this file.
