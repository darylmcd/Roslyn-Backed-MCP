# test-run-compact-execution-detail — opt-in compact `test_run` execution detail

**row:** `test-run-compact-execution-detail` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Models/CommandExecutionDto.cs` — `StdOut`/`StdErr`/`Command`/`Arguments`/`WorkingDirectory` currently non-nullable `string`, unconditionally serialized.
- `src/RoslynMcp.Host.Stdio/Tools/TestRunPublicProjection.cs:416-439` — `PublicCommandExecutionDto`/`PublicTestRunResultDto`, the redacted client-facing projection; mirrors the same field set.
- `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs:202-216` (`RunTests` MCP tool entry point), `246-293` (`RunTestsWithEvictionRetryAsync`), `295-383` (`RunTestsOnceAsync`, builds the anonymous wire object at `349-362` including the `failuresOffset`/`failuresLimit`/`hasMoreFailures`/`failureEnvelope` pagination fields).
- `tests/RoslynMcp.Tests/TestRunPublicProjectionTests.cs` — asserts the redacted `execution` shape; needs new cases for the opt-out path.

## Acceptance

- [ ] `test_run` gains an opt-in parameter (default value must preserve the *current* wire shape — see Notes on polarity) that, when set, omits `execution.stdOut`/`stdErr`/`command`/`arguments`/`workingDirectory` from the response.
- [ ] `failuresOffset`/`failuresLimit`/`hasMoreFailures`/`failureEnvelope` are omitted (or a documented compact equivalent) when `failuresTotal == 0`, gated by the same or a related flag — decide during planning whether this is data-conditional (always trimmed when nothing to paginate) or also flag-gated.
- [ ] `TestRunPublicProjectionTests.cs` covers both the default (current) shape and the new compact shape.
- [ ] Existing tests (`TestRunPublicProjectionTests`, `TUnitMtpNativeTestRunTests`, `ValidationToolsIntegrationTests`) still pass unmodified for the default parameter value — proves no default-shape break.

## Evidence

- GH issue #1421 (godefroi/Mark Parker): measured `test_run`'s clean-pass response as ~2.2x the byte size of raw `dotnet test` stdout on a 527-test TUnit suite (1,362 vs 615 chars); every consumer of MCP tool responses is an LLM agent, so response bytes are a direct recurring cost.

## Context

Issue #1421 proposed two changes. The safe half (suppressing `_meta`'s unconditionally-serialized null observability fields) shipped directly — see `fix/meta-omit-null-fields` — since it required no wire-shape design decision: missing field and explicit `null` are equivalent for any reasonable JSON consumer, and no existing test pinned that null-literal shape.

This row is the remaining, harder half: `execution.stdOut`/`stdErr`/`command`/`arguments`/`workingDirectory` are genuinely redundant with the structured count fields on a clean pass, but trimming them changes which *fields are present*, not just null-vs-omitted — a real wire-shape change on the one published repo in this portfolio (`roslyn-mcp@roslyn-mcp-marketplace`), where an unannounced default-shape break can silently break external consumers.

## Notes

**Polarity correction vs. the issue's proposal — this is the whole reason it wasn't shipped inline:** the issue proposes `includeExecutionDetail: bool = false` (execution detail omitted by default, opt in to get it back). That flips the *default* wire shape for every existing caller who doesn't yet know about the flag — an unannounced breaking change on the published server.

This repo has an established, repeatedly-used convention for exactly this situation (see `AnalysisTools.cs:285`, `AdvancedAnalysisTools.cs:334`, `ImpactSweepTools.cs:35`, `SymbolTools.cs:37,272,671`, `IImpactSweepService.cs:20`, `IReferenceService.cs:18`, `IWorkspaceValidationService.cs:21`): a new opt-in `bool` parameter whose **default value preserves the existing/current shape**, each documented inline as "Default false preserves the vX.Y shape." Follow that convention here — the new parameter's default must reproduce today's verbose `execution` object; passing the non-default value opts into the compact shape. (Naming/exact polarity, e.g. `compact: bool = false` vs `includeExecutionDetail: bool = true`, is a planning decision — either satisfies "default = current shape.")

`ServerToolDtos.cs:11-19` documents the same tension explicitly for a different DTO: some nullable fields deliberately keep explicit `null` because a specific test (`ServerInfoUpdateLatestTests`) pins that exact shape — worth rereading before deciding the pagination-field gating design here.
