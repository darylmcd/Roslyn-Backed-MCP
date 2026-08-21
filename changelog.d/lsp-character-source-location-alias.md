---
category: Fixed
---

- **Fixed:** source-location tools now accept LSP's zero-based UTF-16 `character` field as a pre-binding compatibility alias for the canonical one-based `column`, while rejecting malformed or conflicting dual-field input deterministically. Closes `lsp-character-source-location-alias` and fixes #1128.
