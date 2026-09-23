# dockerfile-run-comment-and-sanctioned-roots-env — Fix Dockerfile run guidance

**row:** `dockerfile-run-comment-and-sanctioned-roots-env` · **pri:** `Low` · **size:** `S`

# dockerfile-run-comment-and-sanctioned-roots-env — Fix Dockerfile run guidance

## Anchors

- `Dockerfile`

## Acceptance

- [ ] The documented docker run command starts the host and loads a mounted workspace.
- [ ] Sanctioned-roots env is set or documented.

## Evidence

- docs S2 audit: Dockerfile:34-35 vs SecurityOptions default (empty roots deny). (doc-audit 2026-09-23)
