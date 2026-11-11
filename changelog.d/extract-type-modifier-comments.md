---
category: Fixed
---

- **Fixed:** `extract_type_preview` no longer drops source comments that sat on a stripped `new`/`virtual` modifier or on a replaced access modifier when it widens or rewrites an extracted member; each comment now reaches the new type once, in its original order. Closes extract-type-modifier-trivia-loss.
