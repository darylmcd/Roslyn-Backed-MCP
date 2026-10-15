---
category: Fixed
---
- **Fixed:** Support class/struct primary-constructor parameter add/remove and preserve call bindings, omitted defaults, expanded parameters, evaluation order, and source trivia across signature changes. Last-parameter struct removal uses an explicit marker constructor; adding to an explicit zero constructor retains it as the initialization owner. Inspect and retain generated marker arguments rather than expecting a literal parameterless edited constructor (ADR 0026).
