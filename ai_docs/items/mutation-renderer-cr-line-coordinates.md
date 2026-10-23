# mutation-renderer-cr-line-coordinates — Preserve XML mutation spans and CR-only line endings

**row:** `mutation-renderer-cr-line-coordinates` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs:469`
- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs:458`
- `tests/RoslynMcp.Tests/SolutionFileMutationEscapingTests.cs`

## Acceptance

- [ ] Map native XML line numbers across CRLF, LF and standalone CR without selecting another element.
- [ ] Preserve existing encoding, preamble, untouched comments/bytes and newline style for appended fragments.
- [ ] Exercise project reference removal/update/imported additions and SLNX membership changes with native premises.
- [ ] Cover other instances of this newline mechanism; qualify any plain-text solution format premise before changing it.

## Evidence

- ElementSpan advances line offsets using only LF, although XML line information counts CR. ApplyXmlPatches selects LF for CR-only inputs. Fix in this PR after causal controls.
