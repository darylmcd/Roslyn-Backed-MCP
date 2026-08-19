# justfile-powershell-recipe-portability — Invoke PowerShell recipes explicitly

**row:** `justfile-powershell-recipe-portability` · **pri:** `Low` · **size:** `S`

## Anchors

- `justfile` — recipes that launch `eng/*.ps1` under the declared Windows and POSIX shells.
- New `tests/RoslynMcp.Tests/JustfilePowerShellRecipeTests.cs`.

## Acceptance

- [ ] Invoke every PowerShell recipe through `pwsh -NoProfile -File` instead of relying on executable bits or a per-script shebang.
- [ ] Preserve recipe arguments, aggregate ordering, and Windows behavior.
- [ ] One table-driven source contract proves every `eng/*.ps1` recipe uses the explicit portable invocation.

## Evidence

- `Justfile` declares a POSIX `sh` shell, but `verify-ai-docs.ps1`, `verify-skills-are-generic.ps1`, and `verify-release.ps1` have no `pwsh` shebang while recipes execute them as `./eng/*.ps1`; Git Bash reproduces the same parser failure found in the bump skill.
