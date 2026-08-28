---
category: Fixed
---

- **Fixed:** the maintainer-local `/update` skill described the plugin cache as "710+ files at 1.29.0", a count from before `.claude-plugin/package-allowlist.txt` existed. The refresh now copies only allowlisted consumer-facing files (52 at 4.1.0), so the documented figure read as a truncated cache during a release. It now names the allowlist as the source of truth and states that a count in the dozens is expected.
