---
category: Fixed
---

- **Fixed:** `eng/stage-review-inbox.ps1`'s retro-report discovery pattern was `*_roslyn-mcp-retro.md`, but every retro report this repo has ever produced is named `*_roslyn-mcp-multisession-retro.md` — none matched, so `/backlog-intake` runs against this repo's own retro output silently found nothing staged. `$filePatterns` and the docstring's recognized-shapes list now include both filename forms.
