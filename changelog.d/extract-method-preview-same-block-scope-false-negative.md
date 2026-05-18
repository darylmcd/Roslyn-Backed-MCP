---
category: Fixed
---

- **Fixed:** `extract_method_preview` rejecting valid single-statement if-block selections with "All selected statements must be in the same block scope" when `endColumn` landed on or adjacent to the closing brace. The statement collector now anchors on statement start position and restricts to direct children of the innermost enclosing block, rather than requiring the full statement span to fall within selection bounds (gh #744).
