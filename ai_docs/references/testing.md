# Testing Reference

<!-- purpose: Test commands, patterns, and coverage expectations for this repo. -->

## Primary Command

- `dotnet test RoslynMcp.slnx --nologo`

## Coverage

- Full release validation (`./eng/verify-release.ps1`) runs tests with **XPlat Code Coverage** and writes Cobertura XML under `artifacts/coverage/`.
- Baseline numbers and test-priority notes: `docs/coverage-baseline.md`.
- CI artifact: `code-coverage` (see `CI_POLICY.md`).

## Build + Test Baseline

1. `dotnet build RoslynMcp.slnx --nologo`
2. `dotnet test RoslynMcp.slnx --nologo`

## Test Project

- `tests/RoslynMcp.Tests/`

## Guidance

- Prefer integration coverage for end-to-end workspace and tool behavior.
- For docs-only changes, run lightweight link/reference checks at minimum.
- For contract/surface changes, include or update tests in the same branch.
