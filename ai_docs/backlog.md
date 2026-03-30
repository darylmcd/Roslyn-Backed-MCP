# Backlog

Canonical location for all unfinished work items. Prioritized by impact.

Last audit: 2026-03-30. Consolidated from deep-review-report.md, mcp-server-audit-report.md (35 issues across 4 solutions), and prior backlog.

---

## P0 — Critical Server Bugs (blocking core workflows)

_All P0 items resolved._

---

## P1 — High-Priority Server Bugs (crashes or wrong output on some solutions)

- [ ] **BUG-08**: `find_reflection_usages` / `get_di_registrations` previously crashed on ITChatBot.sln (2026-03-28 audit). Now functional in v1.2.0 and NuGet content pollution fixed (AUDIT-29). May need retest for crash regression.

---

## P2 — Code Quality Improvements

- [ ] **CODE-11**: Increase test coverage from 49.8% line / 37.6% branch. Focus on high-complexity methods in Roslyn.Services layer. (11 new tests added in this pass — 143→154 total.)

---

## P3 — Server Data Quality & Usability

- [ ] **DATA-01**: `find_shared_members` returns empty for types with readonly fields and static classes. Should include field reads, not just writes.
- [ ] **DATA-02**: `find_unused_symbols` false positives — deduplication and generated-file filtering fixed (AUDIT-19, AUDIT-20). Remaining: consider attribute-aware analysis for framework-invoked methods (e.g. `[Fact]`, `[DataMember]`).
- [ ] **DATA-03**: `TargetFrameworks` shows "unknown" for most projects across all solutions. Fix MSBuildWorkspace TFM resolution. (Still observed in 2026-03-30 audit — AUDIT-07.)
- [ ] **DATA-05**: `get_cohesion_metrics` includes interfaces in LCOM4 results (trivially LCOM4 = method count). `excludeTests` filter added (AUDIT-36). Remaining: filter/flag interfaces separately.
- [ ] **DATA-06**: `get_code_actions` returns empty at most positions. May need additional code fix providers loaded in MSBuildWorkspace.
- [ ] **DATA-07**: Add `limit`/`offset` to `find_references`, `find_type_mutations`, `find_type_usages`, `symbol_relationships`, `callers_callees` — prevent multi-hundred-KB output overflows.
- [ ] **DATA-09**: `extract_interface_preview` missing spaces in generated base type list. Also copies unnecessary usings.
- [ ] **AUDIT-08**: `list_analyzers` (175K-252K chars) and `project_diagnostics` (626K chars on ITChatBot) have no effective pagination. `project` filter barely helps for analyzers (workspace-level). Add `offset`/`limit` params to both.
- [ ] **AUDIT-10**: `split_class_preview` reformats unrelated code during partial class split.
- [ ] **AUDIT-11**: `source_generated_documents` shows duplicate entries for Debug/Release/platform obj folders.
- [ ] **AUDIT-12**: `add_package_reference_preview` generates inline XML without indentation.
- [ ] **AUDIT-14**: Resource listing omits dynamic workspace-specific resources; not discoverable via MCP resource listing.
- [ ] **AUDIT-15**: Resource DTO property name inconsistency (`Name` vs `ProjectName` across resource responses).
- [ ] **AUDIT-06**: `server_info` surface counts (116/6/16) don't match catalog counts. Minor discrepancy.
- [ ] **AUDIT-21**: `fix_all_preview` returns "No code fix provider" for IDE0005, CS8019, CS0414, CA5351, CA1707. IDE and CA analyzers/fixers not loaded in MSBuildWorkspace (SDK-implicit only active during `dotnet build`). `project_diagnostics` reports 1,107 CA warnings on ITChatBot but none are fixable. Workaround: `organize_usings_preview` for unused usings.
- [ ] **AUDIT-23**: `semantic_search` unreliable for generic return type queries (e.g., `Task<bool>`). Returns 0 on NetworkDocumentation; intermittent on ITChatBot (0 on first call, 2 correct on second). Non-generic version works fine.
- [ ] **AUDIT-32**: `get_syntax_tree` exceeds output (142K chars) for methods >100 LOC at `maxDepth=3`. Workaround: use smaller line ranges or `maxDepth=2`.
- [ ] **AUDIT-33**: `analyze_data_flow` variable names not scope-disambiguated. Same-named variables in different loop scopes appear as duplicates in flat list. Add scope/line info per entry.
- [ ] **AUDIT-34**: `find_consumers` misses DI generic argument registration pattern (e.g., `AddSingleton<IFoo, Foo>`). `find_references` captures it but `find_consumers` does not classify it.
- [ ] **AUDIT-35**: `find_unused_symbols` no confidence scoring for public symbols. Flags public enum values and DTO properties as unused. Needs high/medium/low confidence or `excludeEnums`/`excludeRecordProperties` options.

---

## P4 — Feature Additions

- [ ] **FEAT-01**: Add security diagnostic surface — curated mapping of Roslyn diagnostic IDs to OWASP categories with fix guidance. See `ai_docs/prompts/add-security-diagnostic-surface.prompt.md` for full specification.
- [ ] **FEAT-02**: `.editorconfig` write support — current `get_editorconfig_options` is read-only. Add ability to modify style rules programmatically.
- [ ] **FEAT-03**: MSBuild property/item evaluation — query resolved MSBuild properties, conditional values, SDK metadata.
- [ ] **FEAT-04**: Suppression & severity management — programmatic pragma suppression, SuppressMessage, and .editorconfig severity overrides.
- [ ] **FEAT-05**: Maintainability Index — composite metric extending existing complexity_metrics.
- [ ] **REL-10**: Create 512x512 PNG icon for MCP directory listing and manifest. Required by [Anthropic MCP submission guide](https://support.claude.com/en/articles/12922832-local-mcp-server-submission-guide).

---

## Rules

- Keep only open/incomplete items. Remove items when resolved.
- Reprioritize on each audit pass.
- Bug items reference `mcp-server-audit-report.md` for full reproduction details.
- Code items reference `ai_docs/deep-review-report.md` for analysis context.
