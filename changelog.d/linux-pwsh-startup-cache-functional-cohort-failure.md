---
category: Fixed
---

- **Fixed:** The Linux PowerShell startup-cache functional test waits for the native module-cache writer to release its file before verifying the signature, within the existing 30-second budget, so concurrent startup no longer fails on a sharing violation. Closes `linux-pwsh-startup-cache-functional-cohort-failure`.
