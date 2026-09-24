# record-satellites-line-insert-outside-initializer — Insert satellite updates syntactically in record_field_add_with_satellites_preview

**row:** `record-satellites-line-insert-outside-initializer` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:930`

## Acceptance

- [ ] A one-line Clone() => new(){…} gets the new member inside the initializer (no CS1519)
- [ ] Snapshot/Reset satellites are updated
- [ ] Regression test

## Evidence

- 'Evictions = this.Evictions,' inserted after the one-line Clone → CS1519 (reverted). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
