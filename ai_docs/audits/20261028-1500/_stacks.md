# Stacks — refactor-audit 20261028-1500

| Path | Stack | Note |
|---|---|---|
| `tests/RoslynMcp.Tests`, `tests/*Probe`, `tests/RoslynMcp.ShardDiscoveryFixtures` | C# (MSTest) | 494 `.cs` files, ~3.6k test methods |
| `eng/*.ps1`, `eng/*.sh` | PowerShell / shell | 48 files; no PowerShell backend, `rg` and reading |
| `eng/intake-from-issues.py` | Python | single script, out of focus |
| `.github/workflows/*.yml`, `.github/dependabot.yml`, `.github/PULL_REQUEST_TEMPLATE.md` | GitHub Actions YAML | `ci-map`, `ci-durations`, reading |

Excluded (RULES 14): `bin/`, `obj/`, `.tmp/`, `artifacts/`, `TestResults/`.
