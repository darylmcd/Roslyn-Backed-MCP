---
category: Maintenance
---
Broke the `RoslynMcp.Host.Stdio.Middleware` <-> `RoslynMcp.Host.Stdio.Tools` namespace cycle by extracting the shared elicitation-choice contract (`HasElicitation`, `TryElicitChoiceAsync`) into a new `RoslynMcp.Host.Stdio.Elicitation` namespace; `Middleware` and `Tools` now both depend on it instead of on each other. The Middleware-internal static call surface is preserved via thin delegates, so no elicitation test needed editing. Added a namespace-dependency regression test asserting the cycle stays broken.
