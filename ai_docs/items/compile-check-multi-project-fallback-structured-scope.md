# compile-check-multi-project-fallback-structured-scope — expose compile_check's multi-project fallback as a structured field

**row:** `compile-check-multi-project-fallback-structured-scope` · **pri:** `Medium` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:283-289` (fallback trigger + `restoreHint` prose message)
- `src/RoslynMcp.Core/Models/CompileCheckDto.cs` (response DTO — needs `actualScope`/`requestedScope` fields)
- tests under `tests/RoslynMcp.Tests/Services/CompileCheckServiceTests.cs` (or equivalent)

## Acceptance

- [ ] `CompileCheckDto` carries a structured `actualScope` (e.g. `"project"`) vs the caller's `requestedScope` (e.g. `"files"`) so the fallback is programmatically detectable without parsing `restoreHint` text
- [ ] Existing `restoreHint` prose message is preserved for human readability; the structured fields are additive
- [ ] Regression test: a `files[]` scope spanning >1 `.csproj` asserts `actualScope != requestedScope` in the response

## Evidence

- `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2a `compile_check` fallback row + §3 pattern 2. 5 codex sessions, 4 repos (DotNet-Firewall-Analyzer x2, TradeWise, DotNet-Network-Documentation, BioRemote), deterministic — near-identical fallback wording ("compile_check file filter fallback: supplied file scope resolved to multiple projects...") every time, including twice within one session. 7.3s observed latency cost from the widened full-project compile.

## Context

The fallback logic itself is correct — when a `files[]` array crosses project boundaries, compiling the full project(s) and filtering the returned diagnostics back down is the only way to get accurate results. The gap is purely discoverability: the only signal that the caller's narrow scope was silently widened is the prose `restoreHint` text, so a caller has to read and parse a sentence to know whether their fast, narrow-scope request actually ran wide and slow. This is a latency/discoverability issue, not a correctness bug — every occurrence in the retro sample still returned correct, complete diagnostics.

## Notes

A pre-flight "which project(s) does this file set span" check was also proposed in the source report as a way to let callers split multi-project batches into per-project calls proactively — larger scope, worth a separate row if this one's structured-field fix doesn't reduce the observed latency cost enough in practice.
