---
category: Maintenance
---
- **Maintenance:** Repository hygiene: removed a developer's local user-profile path from nine tracked audit, retro, plan and runbook files (`docs/self-hosted-runner.md` now derives paths from `$env:USERPROFILE`). `verify-ai-docs.ps1` now rejects any tracked or pending file that introduces a non-placeholder user-profile path, on every CI route. Closes `local-user-path-leaks-sanitize-and-guard`.
