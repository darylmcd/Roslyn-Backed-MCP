# native-directory-query-cold-win32-truth — native-directory-query-cold-win32-truth

**row:** `native-directory-query-cold-win32-truth` · **pri:** `High` · **size:** `S`

| Field | Value |
|---|---|
| Suggested id | native-directory-query-cold-win32-truth |
| Title | Replace unsupported cold Win32 directory-query test premise with exact native evidence and strict physical admission |
| Priority | High — blocks restore prerequisite verification |
| Regression shape | Fresh regular file plus trailing directory separator can produce first-call native exists=true, while typed identity is File and subsequent identical native query is false. |
| Ownership | Windows API behavior is external; repository owns its test oracle and fail-closed admission contract. |
| Edit targets | tests/RoslynMcp.Tests/NativeConditionProtocolTests.cs; new tests/RoslynMcp.Tests/ConditionInputTruthTests.cs |
| Context anchors | src/RoslynMcp.Roslyn/Services/ConditionInputTruth.cs; NativeConditionEvidence.cs; PhysicalGraphLayout.cs; NativeCompilerContextCertificate.cs |
| Do | With explicit operator approval for the named assertion changes, bind each selected value to its own native invocation evidence. Preserve exact relocated operand checks and strict stable replay controls. Require exact admission refusal for contradictory file/suffix truth. Add deterministic mismatch and authority controls. |
| Acceptance | Same-evaluation native values match exact evidence; stable replay remains strict; inconsistent truth cannot obtain physical authority; deterministic tests fail when mismatch guard is removed; original combined suite passes without retry or timeout changes. |
| Evidence | Scratch native-concurrent-directory-dbffee* raw API diagnostic, independent installed and marshaled PInvoke overloads, 1022/1024 first-call file results32 each, second call uint.MaxValue; all directory results16. Prior cache-empty observation in native-concurrent-directory-327add877987414f8d7bf8601b1359d4. |
| Approval | Explicit operator approval received 2026-10-17 for this specific correction; installed. See ai_docs/reports/20261017-prerequisite-test-contract-decisions.md. |
| Non-goals | No retries/prewarming; no production semantic normalization or authority weakening; no claim to fix externally owned Win32 implementation. |
