---
category: Maintenance
---

- **Maintenance:** `RefactoringService` no longer duplicates `OrchestrationMsBuildXml.GetOrCreateItemGroup` (fixes a latent formatting gap where a freshly-created `<ItemGroup>` was appended without indent/line-ending trivia) and its solution-rebase path now uses a precomputed file-path index instead of a per-changed-document linear scan.
