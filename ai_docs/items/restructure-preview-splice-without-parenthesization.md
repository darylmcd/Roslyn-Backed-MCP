# restructure-preview-splice-without-parenthesization — Parenthesize captured expressions when restructure_preview substitutes placeholders

**row:** `restructure-preview-splice-without-parenthesization` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestructureService.cs:410`

## Acceptance

- [ ] Pattern `__a__ + 1` → goal `__a__ * 3` applied to `count + 1` style input where __a__ binds a binary expression yields a parenthesized result
- [ ] Regression test asserts precedence is preserved for binary/conditional captures

## Evidence

- `__a__ + 1` → `__a__ * 3` produced `count + count * 3` (compiles clean, wrong semantics). PlaceholderSubstituter.VisitIdentifierName returns captured.WithTriviaFrom(node) with no parenthesization. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Use Roslyn's Simplifier-annotated ParenthesizedExpression (add parens + Simplifier.Annotation) so redundant parens are removed afterwards.
