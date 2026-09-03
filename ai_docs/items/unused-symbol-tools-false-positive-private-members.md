# unused-symbol-tools-false-positive-private-members — unused-symbol-tools-false-positive-private-members

**row:** `unused-symbol-tools-false-positive-private-members` · **pri:** `High` · **size:** `S`

# `unused-symbol-tools-false-positive-private-members` — `find_unused_symbols` / `find_dead_fields` report directly-called private members as dead

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:14`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs:377`
- `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs:1`

## Acceptance

- [ ] A `private static` method with a direct call site in its own file is NOT reported by `find_unused_symbols`
- [ ] A `private readonly` field read in its own file is NOT reported by `find_dead_fields`
- [ ] A regression test pins each of the six shapes below against a fixture project
- [ ] If the tools cannot be made accurate, they are demoted out of the default surface and their description states the false-positive rate

## Evidence

- 6 spot-checks, 6 false positives, across 3 projects of an external consumer repo (`C:/Code-Repo/DotNet-Network-Documentation`, commit `b5f38479`) during a `/refactor-audit` run on 2026-09-02. Report: `C:/Code-Repo/DotNet-Network-Documentation/ai_docs/audits/20260902-1443/report.md`
- The consumer repo's own `ai_docs/references/roslyn_unused_symbols_triage.md` already documents an EXPECTED false-positive class for `includePublic=true` (reflection / JSON contracts / test-only / intentional extension points). **The hits below are outside that documented class** — they are private members with direct in-file call sites, which is why this is a defect and not the known caveat.

## Context

`find_unused_symbols` and `find_dead_fields` reported the following as unused/never-read. Every one was re-verified as live by direct source read on 2026-09-03:

| Reported | Ground truth |
|---|---|
| `DiffEngine.DiffDevice` unused | `private static`, called at `Core/Diff/DiffEngine.cs:60`; declared `:156` |
| `DeviceClassifierService.ClassifyByHostname` unused | `private static`, called at `DeviceClassifier/DeviceClassifierService.cs:115`; declared `:256` |
| `DependencyTelemetry.Operation._logger` never read | `private readonly`, read at `Core/Diagnostics/DependencyTelemetry.cs:134,144,152` |
| `DependencyTelemetry.Operation._startTicks` never read | `private readonly`, read at `Core/Diagnostics/DependencyTelemetry.cs:128` |
| `AppConfig._instance` never read | `private static`, read at `Core/Config/AppConfig.cs:20,43,54` |
| `ConfigLoader.PlainStringMappings` never read | read at `Core/Config/ConfigLoader.cs:46` |
| `ChangesSheetBuilder` unused | `find_references` returns a real ref at `Core/Utils/InventoryProjectionService.cs:45` |
| `ClientLogRateLimiter` unused | `find_references` returns a real ref at `Web/ClientLogEndpoints.cs:13` |

The failure spans Core, Web and Parsers, so it is not project-shaped. A `private readonly` field read four times in its own file being reported "never read" is not a visibility or reflection edge case.

**Why this is High and not Low.** The same server ships `remove_dead_code_apply` and `find_dead_locals`. An agent that trusts `find_unused_symbols` and chains it into the removal tool will delete working code — the tool's output is not merely noisy, it is actively unsafe as an input to the mutation surface that sits beside it. The practical effect observed downstream: the consumer audit had to declare its whole DeadCode dimension **degraded** and filed **zero** dead-code rows, because nothing from either tool was trustworthy enough to act on.

## Notes

**Provenance caveat (honesty).** The tool-output half of this evidence was produced earlier in the same session (2026-09-02) while the `roslyn` MCP server was connected; the server has since disconnected, so the false-positive output has **not** been re-demonstrated on 2026-09-03. The ground-truth half (that all eight members are genuinely live) WAS re-verified today by direct source read, and is recorded above with exact line numbers. Reproduce by loading `NetworkDocumentation.sln` and running `find_unused_symbols` / `find_dead_fields` against the Core project.

**Suggested approach.** Check whether the symbol walk resolves references only across documents rather than including the declaring document, or whether it is filtering by accessibility before counting references. The in-file-call-site pattern in every one of the six hits points at one of those two shapes.

**Counterargument.** These tools are marked experimental, and the consumer repo already carries a written triage procedure telling agents to confirm every hit with `find_references` before acting — so a careful operator is already protected. That does not win: the documented caveat covers `includePublic` public-surface shapes, not private members with in-file callers; a 6/6 false-positive rate makes the tool worse than no tool, since it costs a `find_references` call per hit to learn nothing; and the adjacent `remove_dead_code_apply` means the failure mode for a *less* careful agent is deleted working code.

**Blast radius.** The two tools plus any prompt/skill that recommends them (`roslyn-mcp:dead-code` skill). No change to the mutation tools themselves.
