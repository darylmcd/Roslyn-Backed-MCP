# early-kill-matches-cross-read-boundaries — Recognize patterns across output reads

**row:** `early-kill-matches-cross-read-boundaries` · **pri:** `Medium` · **size:** `M` · **verified:** `52f2c418cf5ba982b2a1c0914dd3977655d93eb4`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:253-284`
- `src/RoslynMcp.Core/Services/IDotnetCommandRunner.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:186-192`

## Acceptance

- [ ] Detect the supported early-kill patterns anywhere they occur in either individual output stream, including across arbitrary read boundaries. Prove a held child is stopped for a split marker on both streams; keep per-stream memory bounded and never match across stdout and stderr.

## Evidence

- Base 52f2c418: DotnetCommandRunner scans the latest StreamReader.ReadAsync slice only. MSB3027/3021 can be split across reads and evade detection even when the list was forwarded.

## Context

- 5.x compatibility to re-vet before selection: stable test_run exposes EarlyKillReason and termination classification. Preserve the published fields and their meanings. Separate mechanism from the interface fallback, which discards the list before scanning.

## Notes

Adjacent implementation debt in the same scan loop: DotnetCommandRunner.cs:320 says Regex cannot accept spans, but net10 reference metadata exposes Regex.IsMatch(ReadOnlySpan<char>, int). The current slice-to-string allocation and stale comment should disappear with the corrected bounded scanner.
