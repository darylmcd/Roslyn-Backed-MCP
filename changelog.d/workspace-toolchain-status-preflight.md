---
category: Fixed
---

- **Fixed:** `workspace_load` partial-load UX for legacy .NET Framework and COM-reference projects — `ResolveComReference` and `.NET Core MSBuild` diagnostics are now downgraded from `WORKSPACE_FAILURE` Error to Warning, the summary DTO reports `isReady: false`, and `restoreHint` carries the concrete remediation ("requires Visual Studio MSBuild — remove COM references or load from an environment where msbuild.exe is on PATH") instead of misleading callers toward `dotnet restore`.
