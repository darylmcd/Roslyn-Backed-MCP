# verify-ai-docs-link-gate-false-positives — verify-ai-docs-link-gate-false-positives

**row:** `verify-ai-docs-link-gate-false-positives` · **pri:** `Medium` · **size:** `S`

# verify-ai-docs-link-gate-false-positives — Stop the doc link gate firing on code quotes

## Anchors

- `eng/verify-ai-docs.ps1`

## Acceptance

- The broken-relative-link scan at lines 50-95 ignores any bracket-then-parenthesis sequence that falls inside an inline code span or a fenced code block, so a verbatim code quote no longer produces a link candidate.
- Real broken relative links, broken absolute links, and the all-dot ellipsis placeholder rule at line 77 all still fail with their existing diagnostics. No loosening of genuine link coverage outside code.
- Secondary hardening, shape defect with no observed failure: the placeholder skip at line 63 treats an empty-brace target as a placeholder, matching the documented braces convention. Zero tracked markdown files use that shape today, so this is a correctness completion rather than a fix for a witnessed break.

## Regression

Exercise the gate against an isolated fixture holding four shapes: a fenced block whose code contains a bracket-index expression immediately followed by a parenthesised argument list; an inline code span carrying the same shape; an empty-brace link target; and one genuinely broken relative link outside any code. Only the last must be reported. Follow the existing `tests/RoslynMcp.Tests/ActionlintGateContractTests.cs` precedent for eng-script contract tests.

## Evidence

Provenance: a process observation from the 2026-09-13 retro session, NOT a section-4 retro finding. The report carries no finding id matching this row; section 4 holds 4.1-4.8 and none of them covers the doc gate.

Verified against current source this session rather than inherited: the regex at line 52 runs over `Get-Content -Raw` output with no code exemption, and the placeholder guard at line 63 requires at least one character between the braces. Behavioural proof: a fenced JavaScript snippet using a bracket-index call yields a phantom link target that the gate then reports as a broken relative link, and stripping fenced blocks plus inline code spans before the scan removes 69 link candidates that the gate evaluates across tracked markdown today, none of which is a rendered link. Corroborated by the standing operator note that a literal link example inside backticks still trips the broken-relative-link gate and has to be reworded around.

Report: `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` — session source only; no matching finding id.

## Implementation note

Strip-then-scan: remove fenced blocks and inline code spans from `$content` before the link regex runs, preserving line structure so future line-numbered diagnostics stay accurate, and relax the placeholder guard to allow zero characters between the braces. Both edits stay inside the loop at lines 49-94.

## Bad code surfaced, Directive #3

The link-scan loop hand-rolls markdown parsing over raw text and already carries three special-case skips — braces placeholder, all-dot ellipsis, absolute-drive — each added reactively after a CI false-positive. That accretion is the root cause of this finding. The strip-then-scan restructure replaces the pattern rather than adding a fourth special case.
