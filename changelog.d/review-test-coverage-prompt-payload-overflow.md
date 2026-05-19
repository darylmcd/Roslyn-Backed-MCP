---
category: Fixed
---

- **Fixed:** `review_test_coverage` prompt payload overflow: the rendered prompt now caps the embedded test-discovery list at 50 entries (previously up to 200) using the existing `SerializeTruncatedList` helper, keeping prompt payloads well under the MCP inline cap for large test suites. Closes gh #756.
