# shared-expression-test-console-writeline — Leftover Console.WriteLine in a shared-expression test

**row:** `shared-expression-test-console-writeline` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs:271`

## Acceptance

- [ ] No Console output in the type-position refusal test; assertions unchanged and green.

## Evidence

- shared-expr report bad-code sketch (2026-11-18): `Console.WriteLine("TYPE-POSITION:"...)` inside the catch-when block.
