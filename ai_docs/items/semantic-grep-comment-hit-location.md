# semantic-grep-comment-hit-location — Report the match offset for semantic_grep comment hits

**row:** `semantic-grep-comment-hit-location` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs:233`

## Acceptance

- [ ] TODO at line 70 inside a doc comment starting at line 64 reports line 70 with the TODO in the snippet

## Evidence

- semantic_grep('TODO|FIXME', scope=comments) → RecordFieldAdditionImpactDto.cs line 64 col 4; actual TODO at line 70, not in snippet. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
