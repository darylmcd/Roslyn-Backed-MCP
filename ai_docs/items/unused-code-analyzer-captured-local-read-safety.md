# unused-code-analyzer-captured-local-read-safety — unused-code-analyzer-captured-local-read-safety

**row:** `unused-code-analyzer-captured-local-read-safety` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs`
- `tests/RoslynMcp.Tests/DeadLocalDetectorTests.cs`

## Acceptance

- [ ] Treat a local captured and read from a nested local function as live.
- [ ] Retain the existing genuinely dead-local detection behavior.
- [ ] Add one outer-local / local-function regression that is absent from the dead-local result.

## Evidence

- A live audit reported `observed` dead although semantic references showed its write and read inside the nested `RequestAsync` local function.
