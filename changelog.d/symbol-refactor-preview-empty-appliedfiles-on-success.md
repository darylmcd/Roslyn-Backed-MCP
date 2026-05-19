---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Fixed `apply_composite_preview` returning `success: true` with an empty `appliedFiles` list when the composite preview produced no file-level mutations (gh #750). The tool now returns an explicit error in this case so callers can distinguish a genuine no-op preview from a successful apply. Breaking: callers previously pattern-matching `success=true && appliedFiles=[]` to detect no-op must now check `success=false`.
