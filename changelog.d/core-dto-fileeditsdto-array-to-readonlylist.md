---
category: Fixed
---

- **Fixed:** `FileEditsDto` so two instances holding equal edit sequences now compare equal via record `Equals`/`GetHashCode` (previously always reference-unequal because the `Edits` property was a raw array); `Edits` is now `IReadOnlyList<TextEditDto>`, closing the aliased-mutation surface where a caller could mutate a shared edit array through one `FileEditsDto` reference and silently affect another.
