---
category: Fixed
---
- **Fixed:** `restructure_preview` now parenthesizes a captured expression (and a substituted goal) when it lands in an operator-operand slot, so operator precedence is preserved (`__a__ + 1` -> `__a__ * 3` over `a + b + 1` now yields `(a + b) * 3`, not `a + b * 3`).
