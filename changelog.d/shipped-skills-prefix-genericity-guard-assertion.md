---
category: Fixed
---

- **Fixed:** the shipped-skill genericity gate now enforces prefix-agnostic tool references. `eng/verify-skills-are-generic.ps1` fails on a bare `mcp__roslyn__` literal used inside a "call it / verify it appears in your tool surface" instruction (the canonical note's explicit *examples, not an allowed list* prefixes remain permitted), and asserts the resolve-once-then-pin precheck block stays byte-identical wherever it appears, in both its section and inline-blockquote canonical forms. Files still carrying the pre-fix note are tracked in an enumerated, shrink-ratcheted allowlist rather than silently exempted.
