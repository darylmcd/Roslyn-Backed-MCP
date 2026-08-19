---
category: Fixed
---

- **Fixed:** Scaffolding project-file parse fallbacks no longer log raw exception objects or absolute project paths. Test-framework detection and test-project validation now emit a secret-safe diagnostic (redacted project identity, deterministic outcome, correlation ID) through the shared unexpected-exception projection, while keeping the existing `mstest` default and allow-through behavior.
