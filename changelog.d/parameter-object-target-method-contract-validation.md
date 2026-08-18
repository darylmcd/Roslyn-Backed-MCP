---
category: Fixed
---

- **Fixed:** `parameter_object_preview` now validates the target method's kind and dispatch contract before collecting call sites or storing a preview token. Constructors, destructors, operators and conversions, property/event accessors, overrides, virtual and abstract dispatch roots, interface implementations, `extern`/PInvoke declarations, and partial definition/implementation pairs are refused with an actionable message naming the unsupported contract. Previously these targets produced a redeemable preview that rewrote the declaration while leaving call sites or paired declarations untouched, yielding a compile-breaking partial rewrite.
