---
category: Added
---

- **Added:** `eng/verify-changed-format.ps1`, a changed-file formatter gate wired into the CI `validate` leg and `just ci`. New `IMPORTS`, `IDE1006`, `WHITESPACE`, and `FINALNEWLINE` violations on files a pull request touches now fail the build, while the tracked repository formatter baseline stays explicitly reported rather than suppressed.
