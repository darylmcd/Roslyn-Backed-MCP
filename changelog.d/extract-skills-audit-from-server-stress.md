### Changed

- Moved plugin-skill audit (formerly Phase 16b of `/audit-deep`, then `/mcp-server-stress`) into `/surface-audit` where the static-catalog audit already lives. `/mcp-server-stress` now audits only the running server's surface; `/surface-audit` covers the static catalog plus shipped + maintainer-local skills layered on it (walks both `skills/*/SKILL.md` and `.claude/skills/*/SKILL.md`). Closes `extract-skills-audit-from-server-stress`.
