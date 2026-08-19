## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:543` — the four refusal throws added by PR #1281
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs:1013` — the new 249-line test block

## Acceptance

- Each constructor-topology refusal throw added in PR #1281 has a test asserting its specific message.
- Currently only the primary-constructor refusal is covered (`ExtractType_PrimaryConstructor_RefusesWithTopologyMessage`).

## Evidence

Traced in code during PR #1281 review: the diff adds four refusal throws in `InjectFieldAndCtorParameter`; only one is exercised. The remaining error paths have no test in the new block.

Source: code-quality review of PR #1281 (initiative `type-extraction-composition-constructor-coverage`, sweep 20260819T180531Z).
