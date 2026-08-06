---
category: Maintenance
---

- **Maintenance:** consolidated the five duplicated error-envelope anonymous-object literals in `ToolErrorHandler.FormatErrorResponse` into a single `BuildErrorEnvelope` helper — no observable behavior change; adding a new structured error field going forward is now a one-line call instead of a new literal.
