---
category: Fixed
---

- **Fixed:** Policy-only documentation pull requests now run the complete release verifier on the artifact-owning leg, so docs-tier inputs (`THIRD-PARTY-NOTICES.md`, the packed `src/RoslynMcp.Host.Stdio/README.md` registry marker, `CHANGELOG.md`, allowlisted plugin files) can no longer bypass the license, registry-readiness, package-allowlist or version gates; `CI_POLICY.md` now declares the required `validate` check as the merge proof.
