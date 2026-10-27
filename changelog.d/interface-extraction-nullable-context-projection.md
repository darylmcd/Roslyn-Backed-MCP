---
category: Fixed
---

- **Fixed:** Interface extraction preserves nullable annotation contexts on emitted signatures across standalone, cross-project, dependency-inversion and extract-and-wire previews without changing warning policy. Needed `#nullable … annotations` directives are whole lines in the document's line endings, scoped to the members that need them and shared by adjacent members; output is byte-identical when contexts already match. Closes `interface-extraction-nullable-context-projection`.
