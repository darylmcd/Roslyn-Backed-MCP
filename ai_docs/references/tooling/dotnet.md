# Dotnet Tooling Reference

<!-- purpose: Common dotnet CLI entry points used in this repository. -->

## Core Commands

- Build: `dotnet build RoslynMcp.slnx --nologo`
- Test: `dotnet test RoslynMcp.slnx --nologo`
- Run host: `dotnet run --project src/RoslynMcp.Host.Stdio`
- Verify release: `./eng/verify-release.ps1`

## Notes

- Prefer solution-level build/test for merge-ready handoff unless intentionally scoping validation.
- Use `--nologo` in automation-oriented command examples for cleaner logs.
