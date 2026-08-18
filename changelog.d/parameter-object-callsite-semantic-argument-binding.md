---
category: Fixed
---

- **Fixed:** `parameter_object_preview` mis-binding call-site arguments. Argument-to-parameter association is now derived from Roslyn's `IInvocationOperation` bindings instead of lexical argument position, so calls using in-position named arguments, reduced extension-method receivers, or an ungrouped trailing `params` tail rewrite correctly instead of dropping or transposing arguments. Invocation shapes that cannot be mapped completely — including out-of-order named arguments whose non-constant expressions would change evaluation order, and method-group or non-invocation references — are now refused before a preview token is stored.
