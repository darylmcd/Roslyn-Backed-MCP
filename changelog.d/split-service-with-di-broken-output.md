---
category: Fixed
---

- **Fixed:** `split_service_with_di_preview` emitting non-functional partition/facade code: instance fields are now migrated into the correct partition types (with a synthesized constructor when needed), and `async` modifiers are stripped from facade forwarding stubs so `ValueTask`/`Task` return types compile correctly (gh #766).
