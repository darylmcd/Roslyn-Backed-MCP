# using-directive-synthesizer-static-distinctby-name-tostring — UsingDirectiveSynthesizer DistinctBy uses Name.ToString for static usings

**row:** `using-directive-synthesizer-static-distinctby-name-tostring` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveSynthesizer.cs:73`

## Acceptance

- [ ] Two static usings of the same type in different spellings (escaped, interior trivia) are deduplicated; semantic-model fixture test.

## Evidence

- reloc-b report sketch 4: same Name.ToString idiom slice B1 replaced elsewhere; left untested because it needs a semantic-model fixture.
