---
category: Fixed
---

- **Fixed:** `extract_type_preview` now refuses, with a structured `blockingDependencies` entry and before any preview token is stored, to move a member that overrides a base-class member (including `sealed override`), an `abstract` member, or a `virtual` member that has overrides in the solution; previously it silently stripped the modifier, changing dispatch (for example `ToString()` fell back to `object.ToString`) or leaving the source without a required implementation (CS0534). Closes `extract-type-override-modifier-stripped`.
