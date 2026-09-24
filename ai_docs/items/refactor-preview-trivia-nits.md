# refactor-preview-trivia-nits — Fix trivia nits in extract_interface and remove_dead_code previews

**row:** `refactor-preview-trivia-nits` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:133`
- `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs:129`

## Acceptance

- [ ] extract_interface keeps 'class G4Fixture : IG4Fixture' on one line
- [ ] remove_dead_code leaves no whitespace-only line

## Evidence

- 'class G4Fixture\n : IG4Fixture'; dead-code removal leaves a '    ' line. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
