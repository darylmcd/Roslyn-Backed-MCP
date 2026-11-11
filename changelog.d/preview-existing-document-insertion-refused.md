---
category: Fixed
---

- **Fixed:** Text edit preview and apply accept valid C# edits in files with titled regions or warning directives. Syntax checks still reject parser recovery tokens and invalid edits. The experimental `preview_multi_file_edit` and `symbol_refactor_preview` tools report a safe correction when syntax validation refuses an edit. Closes `preview-existing-document-insertion-refused`.
