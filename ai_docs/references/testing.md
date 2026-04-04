# Testing Reference

<!-- purpose: Test commands, patterns, and coverage expectations for this repo. -->

## Primary Command

- `dotnet test RoslynMcp.slnx --nologo`

## Build + Test Baseline

1. `dotnet build RoslynMcp.slnx --nologo`
2. `dotnet test RoslynMcp.slnx --nologo`

## Test Project

- `tests/RoslynMcp.Tests/`

## Guidance

- Prefer integration coverage for end-to-end workspace and tool behavior.
- For docs-only changes, run lightweight link/reference checks at minimum.
- For contract/surface changes, include or update tests in the same branch.
