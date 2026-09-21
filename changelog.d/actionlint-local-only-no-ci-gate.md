---
category: Changed
---

- **Changed:** CI now runs the checksum-pinned `eng/verify-actionlint.ps1` on the hosted artifact-owner leg, making the workflow-lint gate that `just ci` already ran a real pull-request merge gate.
