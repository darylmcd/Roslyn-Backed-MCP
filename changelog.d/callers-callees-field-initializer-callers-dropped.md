---
category: Fixed
---

- **Fixed:** `callers_callees`, `impact_analysis`, and `find_references` now attribute calls inside field and event-field initializers to the declaring field or event instead of dropping them (or reporting a null `containingMember`); a multi-declarator field maps each initializer to its own declarator. (`callers-callees-field-initializer-callers-dropped`)
