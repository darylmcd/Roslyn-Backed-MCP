# project-metadata-parser-broad-exception-fallbacks — Classify expected metadata failures explicitly

**row:** `project-metadata-parser-broad-exception-fallbacks` · **pri:** `Medium` · **size:** `S` · **deps:** `hosted-dotnet-acquisition-origin-access-denied`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:101` — Target-framework evaluation catches every Exception and returns fallback metadata.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:244` — Metadata evaluation has another broad exception fallback.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:304` — A third broad exception fallback converts unexpected failures into metadata/null.
- (new) `tests/RoslynMcp.Tests/ProjectMetadataParserFailureClassificationTests.cs`

## Acceptance

- [ ] The three fallback boundaries handle only expected evaluation/read/parse failures; unexpected programming failures propagate instead of becoming plausible metadata or null.
- [ ] Preserve intentional expected-failure fallback behavior and diagnostics; qualify both expected classification and unexpected-exception propagation across the same catch mechanism.
- [ ] Observe the propagation regression fail before repair; obtain independent review, scoped/hosted checks and the serialized delivery gate before closure.

## Scope

- One broad-catch mechanism in one production file, one test file and a fragment.
- Independent follow-up; not a prerequisite of PR1815 or the ordered restore chain.

## Evidence

- Fresh independent implementation review of PR1815 head b0db3c6d3c464ea08ce8943bafe0acd1131621b3 identified all three pre-existing boundaries while verifying the separate JSON-kind fix.
- Do not expand the JSON-kind PR with this evaluation-failure mechanism.
- Current implementation expands the same mechanism to the fourth assets-evaluation boundary and shared helper. Independent plan and implementation reviews PASS on head0525616808eb33fccd7dd539dc24b78cdf46833d.
- Draft [PR1835](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1835): baseline11failures/5controls; final integrated166/166passed across10classes, no skips; cold bootstrap2/2passed; compile8/8zeroerrors/warnings.
- Held before merge: all six hosted matrix legs fail during external SDK installation HTTP409; current-main run40224224815 reproduces setup failure. Tracking row hosted-dotnet-acquisition-origin-access-denied owns acquisition resolution and final qualification. No retry or gate bypass.
