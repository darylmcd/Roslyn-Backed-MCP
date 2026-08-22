---
category: Fixed
---

- **Fixed:** `test_discover`/`test_run` now recognize TUnit test projects. TUnit ships its own `Microsoft.Testing.Platform` host instead of the classic VSTest adapter, so a TUnit project references the `TUnit` package rather than `Microsoft.NET.Test.Sdk` and never stamps `<IsTestProject>` — `ProjectMetadataParser.IsTestProject` didn't recognize it, so these projects were invisible ("0 test projects found").
