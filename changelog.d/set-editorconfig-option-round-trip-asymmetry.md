---
category: Fixed
---

- **Fixed:** `get_editorconfig_options` now returns keys written by `set_editorconfig_option` for analyzer ids not reported by any loaded analyzer (e.g., `dotnet_diagnostic.CA9999.severity`). The on-disk section matcher in `EditorConfigService.GetOptionsAsync` used a literal `.cs` substring heuristic that dropped `[*.{cs,csx,cake}]` headers; replaced with a proper brace-expansion-aware matcher covering `[*]`, `[*.cs]`, `[*.csx]`, `[*.cake]`, `[**.cs]`, and brace lists (`set-editorconfig-option-round-trip-asymmetry`).
