---
category: Maintenance
---

- **Maintenance:** dropped never-read `ILogger<T>` field and constructor parameter from 4 `RoslynMcp.Roslyn.Services` types (`BulkRefactoringService`, `CodeMetricsService`, `CompletionService`, `ConsumerAnalysisService`). Updated direct-`new` callers in `CodeMetricsNestingTests` and `TestServiceContainer` to match the new ctor signatures. Batch 1 of 3 — unblocks dependent batches 2 and 3. Closes `dead-logger-fields-roslyn-services-batch-1` (PR #473).
