---
category: Changed
---

- **Changed:** Structured-log event ids now come from one catalog (`LogEvents`) published in `docs/stdio-client-integration.md`. Five ids that all collided on `eventId` 1 moved: `LogTransportDisconnected` 1002, `LogCommandExecuted` 3001, `LogProcessTreeKillFailed` 3002, `LogProviderExecutionFailed` 4001, `LogProviderEnumerationFailed` 4002. `eventName` is unchanged for every event and ids 1-5, 1001 and 2101-2104 keep their numbers, so JSON-lines consumers should select on `eventName`.
