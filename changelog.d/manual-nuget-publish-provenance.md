---
category: Fixed
---

- **Fixed:** the manual NuGet publisher (`eng/publish-nuget.ps1`) now runs the publish-mode release gate, packs a fresh package into an owned staging directory from the canonical six-file version, rejects a mismatched `-Version`, supports validate-only `-NoPush`, and fails on a nonzero `dotnet nuget push` exit instead of printing `Done.` after a failed push.
