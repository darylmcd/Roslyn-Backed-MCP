---
category: Changed
---

- **Changed:** Release policy (ADR 0043): a stable-tool fix that turns a silently wrong success into a `Success:false` refusal carried in the existing response fields (no new field, error category or thrown `InvalidOperation`) ships in a minor as `Fixed` under Rule 1. Migration: callers already check `Success`; inputs that were silently mishandled now return a refusal.
