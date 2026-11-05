---
category: Fixed
---

- **Fixed:** Extracted interfaces keep parameter semantics on every route: a contract attribute beside a default rewritten for the interface no longer throws, constant defaults naming a `nameof`, a source-assembly or non-public member are written as their value (negative enum values parenthesized), a default naming a constant of an otherwise unreferenced type imports its namespace, and a public `__arglist` method is refused with a public message naming the method instead of failing every extraction of its type with an internal error. Covers `cross-project-interface-parameter-semantics`.
