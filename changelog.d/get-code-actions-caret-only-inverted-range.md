---
category: Fixed
---

- **Fixed:** `get_code_actions` caret-only calls (no `endLine`/`endColumn`) whose `startColumn` landed past the line's last character no longer throw `ArgumentOutOfRangeException` from a `TextSpan.FromBounds` inversion — default-end is now clamped to `max(startPosition, lineEnd)`, falling back to a zero-width selection past EOL (`CodeActionService`). (`get-code-actions-caret-only-inverted-range`)
