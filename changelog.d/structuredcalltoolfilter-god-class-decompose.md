---
category: Maintenance
---

- **Maintenance:** Split `StructuredCallToolFilter`'s elicitation-allowlist policy (`IsElicitationAllowedFor`/`IsWorkspaceIdRecoveryAllowedFor`/`IsWorkspaceIdAutoResolveAllowedFor`/`HasElicitation`/`IsSensitiveFieldName`) into a new `ElicitationAllowlistPolicy` class, and its ambient-metrics recording (`RecordAutoResolution`/`RecordAutoLoadElapsed`/`RecordElapsed`) into a new `CallMetricsRecorder` class, reducing the god-class's LOC. `StructuredCallToolFilter`'s public static API is unchanged (thin delegates preserve every existing call site); adds `ElicitationAllowlistPolicyTests` covering the extracted policy directly.
