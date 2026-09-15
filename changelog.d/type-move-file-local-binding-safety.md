---
category: Fixed
---

- **Fixed:** Type-move previews refuse file-local declarations and dependencies before publishing a token, and conservatively refuse source files containing directives because moving their syntax can change compiler context or split directive pairs. Move the whole file with `move_file_preview` or isolate its directive context before retrying. Unrelated file-local siblings remain supported. Refreshed type-move tool ownership documentation. Closes `type-move-file-local-binding-safety`, `type-move-directive-context-preservation`, and `type-move-tool-owner-comment-refresh`.
