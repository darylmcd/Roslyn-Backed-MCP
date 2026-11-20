# raw-wire-result-extraction-dedupe — Share raw JSON-RPC result extraction

**row:** `raw-wire-result-extraction-dedupe` · **pri:** `Low` · **size:** `S`

## Anchors

- (new) New `tests/RoslynMcp.Tests/Helpers/RawJsonRpcTranscriptAssertions.cs`.

## Acceptance

- [ ] Provide one transcript helper that returns cloned result/error frames for a request id and fails on duplicates or missing frames.
- [ ] Migrate both wire suites without changing their protocol matrices or assertions.
- [ ] Keep notification frames explicitly distinguishable from request responses.
- [ ] Both existing raw-wire matrices remain green through the shared extractor.

## Evidence

- Both suites carry an equivalent `FindSingleNewResult` parser, and planned protocol rows would otherwise copy it again.
