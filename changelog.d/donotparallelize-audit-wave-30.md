---
category: Maintenance
---

- **Maintenance:** Re-audited `[DoNotParallelize]` opt-outs on `TestReferenceMapServiceTests.cs`, `ToolCallErrorWireContractTests.cs` and `ToolDispatchTests.cs`. Removed the class-level opt-outs on the first two after repeated concurrent runs proved them safe: one runs read-only queries against the synchronized shared-workspace cache, the other uses only harness-private services and temp paths. Retained the three method-level opt-outs in `ToolDispatchTests.cs` and documented their dependency: they overwrite the process-global `SecurityOptionsSnapshot` that every `*_apply` token redemption reads. Closes `donotparallelize-audit-wave-30`.
