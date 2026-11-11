# relocation-using-semantic-identity — Match relocation imports by namespace identity

**row:** `relocation-using-semantic-identity` · **pri:** `Medium` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:614-615`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:659-660`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] An equivalent escaped or trivia-separated import participates in the same relocation decision as its canonical spelling. A public preview that empties the old namespace removes its obsolete import without duplicating the destination import and compiles after apply.

## Evidence

- The using rewrite compares syntax Name.ToString() against symbol display. Equivalent spellings can evade removal or destination import detection.
