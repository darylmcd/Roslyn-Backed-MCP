---
category: Maintenance
---

- **Maintenance:** Trimmed method-level `[Description]` text on the test-and-impact tools (`test_coverage`, `get_test_coverage_map`, `test_reference_map`, `symbol_impact_sweep`, `preview_record_field_addition`) to ~200-char capability statements, cutting ~1,000 chars (~250 tokens) from every `tools/list` response. Response-field roll-calls, payload-size advice, and pagination detail moved to XML `<remarks>` and were already stated on the parameter descriptions — no capability or discriminating trigger was dropped. Added a slice-scoped description-length regression.
