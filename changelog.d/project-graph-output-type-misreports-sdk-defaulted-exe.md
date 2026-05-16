---
category: Fixed
---

- **Fixed:** `project_graph` silently misreporting `outputType: "Library"` for ASP.NET Core Web (`Microsoft.NET.Sdk.Web`) and Worker (`Microsoft.NET.Sdk.Worker`) projects that omit an explicit `<OutputType>` element. Now populated via MSBuild property evaluation (fixes gh #773).
