# Backlog sweep plan — 20260512T135106Z

**Generated:** 2026-05-12T13:51:06Z
**Backlog snapshot:** 2026-05-12T13:51:06Z (`ai_docs/backlog.md` updated_at)
**Mode:** `/backlog-sweep:prepare count=25`
**Pipeline phase:** skeleton (Phase A complete; Phases B-G pending)
**Cycle:** 0 / 2
**Addenda:** loaded (`ai_docs/prompts/backlog-sweep-addenda.md`)
**Selected:** 22 initiatives (max eligible after excluding 7 Reserved-for-contributor rows; user requested count=25)

## Selection notes

- Source backlog has 29 actionable rows (2 High + 5 Medium + 22 Low − 7 Reserved-for-contributor). All 22 non-Reserved rows are selected.
- Reserved rows excluded per planner Step 1 hard-skip: `add-project-reference-self-reference-not-rejected`, `compile-check-project-filter-miss-no-error-envelope`, `find-duplicated-methods-mcp-wrapper-false-positive`, `get-prompt-text-multi-step-required-param-errors`, `goto-type-definition-builtins-invalidoperation`, `test-run-unfiltered-no-failure-envelope`, `workspace-load-prewarm-double-nullable`.
- Defer rows excluded: 5 (per backlog `## Defer` section).
- Prior plan `20260511T184004Z_backlog-sweep` completed cleanly (status: completed, 19 merged + 4 obsolete + 2 deferred); no in-flight initiatives. Duplicate-check ran during /backlog-intake earlier today and dropped `get-syntax-tree-byte-cap-not-enforced` as a duplicate of obsolete `get-syntax-tree-maxtotalbytes-not-enforced` (pre-shipped per 2026-05-12 re-vet).
- Ranking order: correctness risk (server-impacting bugs > skill-side coverage > docs-only) → blast radius → feasibility.
- Rule 1 bundle candidates probed and rejected: (workspace-close-with-drain, parallel-mode-workspace-cap) share `WorkspaceManager.cs` but address different code paths (close vs load cap); (parameter-naming-canonicalization, skill-prompts-deprecated-workspace-load-param-name-cleanup) split server-side vs skill-side discipline; (audit-and-refactor-skills-roslyn-self-check, routine-flows-wrap-csharp-work-with-roslyn-bookends) target different skill prompt files. All marked `bundleCandidatePeers: []`.

## Initiatives

### 1. parallel-mode-workspace-cap-lru-or-raise

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 2. routine-flows-wrap-csharp-work-with-roslyn-bookends

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 3. workspace-close-with-drain-processes-for-teardown

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 4. workspace-health-csharp-applicability-classifier

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 5. parameter-naming-canonicalization-experimental-surface

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 6. promote-tier-supports-prompts-and-resources

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 7. test-related-column-required-schema-mismatch

_Skeleton placeholder — Phase B deepener will fill this stanza. **Note:** prior plan deferred this row with the note that the diagnosis was suspect (column required only when filePath+line are passed). Deepener: re-read SymbolLocatorFactory.cs:55-77 and the schema before drafting Diagnosis._

### 8. move-type-to-project-preview-leaks-projectid-tokens

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 9. document-symbols-accepts-symbol-handle

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 10. editorconfig-write-no-auto-invalidation

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 11. set-project-property-preview-directory-build-props-blindness

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 12. eternal-experimental-age-audit

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 13. audit-and-refactor-skills-roslyn-self-check

_Skeleton placeholder — Phase B deepener will fill this stanza. **Note:** companion to initiative 2 (routine-flows-wrap-csharp-work-with-roslyn-bookends); deepener: confirm scopes don't overlap._

### 14. bump-skill-double-version-string-detect

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 15. roslyn-skill-prompts-exceed-read-token-cap

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 16. surface-test-skill-self-correction-observability

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 17. skill-namespace-and-semantic-search-discoverability

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 18. skill-prompts-deprecated-workspace-load-param-name-cleanup

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 19. filepaths-array-vs-stringified-tool-description-clarification

_Skeleton placeholder — Phase B deepener will fill this stanza._

### 20. list-analyzers-totalrules-variance

_Skeleton placeholder — Phase B deepener will fill this stanza. **Weaker evidence — defer if deepener cannot reproduce.**_

### 21. compile-check-not-connected-raw-transport-error-envelope

_Skeleton placeholder — Phase B deepener will fill this stanza. **Weaker evidence — defer if deepener cannot reproduce.**_

### 22. scaffolding-service-split-by-scaffold-type

_Skeleton placeholder — Phase B deepener will fill this stanza. **Weaker evidence — defer if no concrete bug motivates the pure organizational refactor.**_
