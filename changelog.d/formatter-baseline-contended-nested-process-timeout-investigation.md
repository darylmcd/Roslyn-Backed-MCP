---
category: Fixed
---

- **Fixed:** the formatter-baseline generator no longer stalls under host contention — spawned `dotnet` children now disable MSBuild node reuse, and the contract test's stdout/stderr drain is bounded on the success path so a lingering handle-inheriting descendant can no longer hang the harness past the nominal timeout.
