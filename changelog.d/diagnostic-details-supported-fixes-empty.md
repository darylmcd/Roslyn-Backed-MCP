---
category: Fixed
---

- **Fixed:** `diagnostic_details.supportedFixes` now enumerates `RegisterCodeFixesAsync` actions from the loaded `ICodeFixProviderRegistry` (covering both the static CSharp.Features pack and per-project analyzer references) instead of the hardcoded CS8019/IDE0005 map that left CA1826/ASP0015/MA0001/SYSLIB1045 silently empty while the tool description advertised "curated fix options." When no providers are loaded, `supportedFixes=[]` is paired with a `guidanceMessage` pointing at `get_code_actions` + `preview_code_action`, and the tool description now matches reality regardless of analyzer pack (`diagnostic-details-supported-fixes-empty`).
