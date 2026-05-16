---
category: Fixed
---

- **Fixed:** `guided_extract_interface` prompt overflow — `get_prompt_text` for this prompt now returns a bounded response by capping the embedded document-symbol list (50 entries) and project graph (20 projects), preventing MCP inline payload cap overflow on 9+ project solutions. The previous implementation embedded full `JsonSerializer.Serialize` output for both the `IReadOnlyList<DocumentSymbolDto>` and the entire `ProjectGraphDto`, scaling linearly with workspace size and exceeding the inline payload cap (~30+ KB observed) on larger solutions. Mirrors the truncation pattern already used by `analyze_dependencies` (fixes gh #776).
