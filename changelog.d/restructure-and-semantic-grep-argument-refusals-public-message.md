---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Preserve safe argument guidance for composite refactors, structural rewrites and semantic searches. Null composite operations now emit InvalidOperation / InvalidOperationException instead of InternalError; null partition entries emit InvalidArgument / ArgumentException instead of InternalError. Null member names change from ArgumentNullException to ArgumentException; blank member names change from InvalidOperation to InvalidArgument. Migration for the next major release: supply non-null operations and partitions with nonblank member names, and handle these refusals as request corrections. Existing null record-field ArgumentNullException identity and regex guidance remain unchanged. See [ADR 0016](../docs/decisions/0016-malformed-refactoring-input-refusals.md).
