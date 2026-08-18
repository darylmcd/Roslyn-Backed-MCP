---
category: Maintenance
---

- **Maintenance:** temporarily bypassed `eng/verify-breaking-version-bump.ps1` to ship the next release as `3.0.1` (patch) despite pending `Changed — BREAKING` fragments — an explicit, twice-confirmed operator override, not a policy change. The pending breaking changes (protocol-logging retirement, MCP SDK 2.x wire-contract reconciliation, tool error-envelope redaction) genuinely change client-visible behavior and are documented as `Changed — BREAKING` in `3.0.1` regardless of the version number; consumers pinned to `^3.0` should review them before upgrading as if this were a major release. The gate bypass is reverted in the commit immediately following the `v3.0.1` tag's confirmed NuGet publish.
