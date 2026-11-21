# release-policy-line-pins-cite-heading — Cite release-policy sections by heading, not line number

**row:** `release-policy-line-pins-cite-heading` · **pri:** `Low` · **size:** `S` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `docs/decisions/0011-v5-major-release-contract.md:55`
- `docs/product-contract.md:159`
- `tests/RoslynMcp.Tests/LocationDtoMigrationTests.cs:113`
- `.claude/skills/bump/SKILL.md:22`
- `.claude/skills/release-cut/SKILL.md:27`

## Acceptance

- [ ] `docs/decisions/0011-v5-major-release-contract.md:55,74`, `docs/product-contract.md:159` and the comment at `tests/RoslynMcp.Tests/LocationDtoMigrationTests.cs:113` cite `docs/release-policy.md` § Versioning And Deprecation by heading instead of `docs/release-policy.md:38`; if any test asserts the line number, it finds the heading instead.
- [ ] `.claude/skills/bump/SKILL.md:22` and `.claude/skills/release-cut/SKILL.md:27` ("major = breaking changes to stable tool contracts") gain a pointer to Rule 1 / Rule 2 in `docs/release-policy.md` § Corrections And Security Removals.

## Evidence

- The line-38 pin forced PR 2168 to keep `docs/release-policy.md` line counts stable; `docs/release-policy.md:38` is the "Breaking a stable request/response contract is a major-version change" bullet inside § Versioning And Deprecation (heading at `docs/release-policy.md:32`).
- `.claude/skills/**` are repo-only maintainer skills (never shipped), so the pointer needs no generic-skill gate.
