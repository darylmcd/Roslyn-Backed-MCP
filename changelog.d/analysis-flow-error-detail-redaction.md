---
category: Fixed
---

- **Fixed:** `analyze_data_flow`, `analyze_control_flow`, and `extract_method_preview` no longer publish raw Roslyn exception text in their failure messages. Failures now carry the operation, the requested line range, the actionable remediation guidance, and a correlation ID via the shared `PublicExceptionDetailPolicy` projection; full exception detail stays in the server-only diagnostic sink. Error message text for these three failure paths changed.
