## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:523` — the hand-copied literal
- `src/RoslynMcp.Roslyn/Services/SemanticGrepService.cs:29` — `InvalidRegexSentinel`, `public const`, zero consumers

## Acceptance

- `BuildSafeArgumentMessage`'s regex arm matches on `SemanticGrepService.InvalidRegexSentinel` (Ordinal) rather than a hand-copied substring, so renaming the sentinel is a compile error rather than a silent behavior change.
- Existing redaction tests pass unchanged; `SemanticGrep_OtherPatternParameters_KeepGenericFallback` still receives the generic fallback.

## Evidence

From the Step 8b code-quality review of PR #1300. The guard hardcodes `rawMessage.Contains("not a valid .NET regular expression")` — a hand-copied substring of `SemanticGrepService.InvalidRegexSentinel`, which the SAME PR declared `public const` expressly as the cross-assembly contract.

`RoslynMcp.Host.Stdio.csproj:59` already `ProjectReference`s `RoslynMcp.Roslyn`, so the const is directly referenceable. Nothing consumes it — not even the new tests, which re-spell the text a third time. The coupling is comment-tracked where it could be compiler-tracked.

Source: code-quality review of PR #1300, sweep 20260819T180531Z.
