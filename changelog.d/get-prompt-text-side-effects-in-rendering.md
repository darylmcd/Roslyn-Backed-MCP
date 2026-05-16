---
category: Fixed
---

- **Fixed:** `get_prompt_text` side effects — `debug_test_failure` and `security_review` prompts now return pure instruction templates instead of eagerly invoking `dotnet test` and NuGet vulnerability scans during rendering. The previous implementations called `ITestRunnerService.RunTestsAsync` and `INuGetDependencyService.ScanNuGetVulnerabilitiesAsync` (which spawns `dotnet list package --vulnerable`) on every prompt fetch, violating the `get_prompt_text` contract of pure template substitution. Callers now invoke `test_run` / `security_analyzer_status` / `security_diagnostics` / `nuget_vulnerability_scan` explicitly and pass the structured results back into the analysis step (fixes gh #772).
