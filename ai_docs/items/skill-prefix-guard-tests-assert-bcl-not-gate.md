# skill-prefix-guard-tests-assert-bcl-not-gate — make the skill-prefix guard tests exercise the gate

**row:** `skill-prefix-guard-tests-assert-bcl-not-gate` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Skills/SkillPrefixGenericityTests.cs:174`
- `tests/RoslynMcp.Tests/Skills/SkillPrefixGenericityTests.cs:191`
- `eng/verify-skills-are-generic.ps1`

## Acceptance

- [ ] `CanonicalBlockIdentity_DetectsASingleCharacterMutation` feeds the mutated text through the same Extract + ordinal-compare path the real assertion uses and asserts the drift list is non-empty — not `SequenceEqual` on a locally-mutated clone.
- [ ] `CanonicalBlockIdentity_AllowsPerSkillTrailingProse` passes the block's real `terminatorPattern` (`^## `) rather than `null`, so the terminator-driven extraction that implements the trailer allowance is actually exercised.
- [ ] A truncated-block case asserts a drift entry is produced (the `extracted.Count < Text.Length` branch).
- [ ] Casing semantics unified: `exemptSpans` and `canonicalPrecheckBlocks.anchorPattern` carry `(?i)` like `imperativePatterns`, or the PowerShell passes switch to `-cmatch`, so the PS gate and its C# echo cannot disagree.

## Evidence

Cold code-quality review of PR #1353 read all the new test methods: the mutation test clones `block.Text`, applies `string.Replace`, and asserts the clone is not `SequenceEqual` — exercising BCL semantics, not the gate. The trailer test builds `withTrailer` via `Concat` then asserts an equality `Concat` guarantees, and calls `Extract(..., terminatorPattern: null)`. Separately, `imperativePatterns` are `(?i)`-prefixed while `exemptSpans`/`anchorPattern` are not; PowerShell `-match` is case-insensitive by default while `Regex.IsMatch` is not.

## Context

The byte-identity guard shipped in PR #1353 (sweep `20260825T151721Z`) is the mechanism that stops the canonical connectivity-precheck block drifting across ~17 shipped skills. Its own negative-path tests do not currently exercise it, so the guard could regress without a test failing.
