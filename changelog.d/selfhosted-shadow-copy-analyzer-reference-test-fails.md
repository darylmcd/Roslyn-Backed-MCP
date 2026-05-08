---
category: Fixed
---

- **Fixed:** `WorkspaceManager.LoadAsync` now accepts an optional `IDictionary<string,string> globalProperties` parameter that flows MSBuild global property overrides (e.g. `Configuration=Release`) into `MSBuildWorkspace.Create`. Without this, `MSBuildWorkspace` defaulted to `Configuration=Debug` evaluation, which dropped `ProjectReference OutputItemType="Analyzer"` entries on Release-only checkouts (such as the self-hosted Windows CI runner) because the Debug `TargetPath` did not exist on disk. The new `SelfHostedWorkspace_AnalyzerReference_Loads_From_Shadow_Copy` test detects the host's actual build configuration from `AppContext.BaseDirectory` and passes it through. When global properties are set, the load also opts out of session deduplication and the cache fast-path so the new evaluation always runs. Closes `selfhosted-shadow-copy-analyzer-reference-test-fails`.
