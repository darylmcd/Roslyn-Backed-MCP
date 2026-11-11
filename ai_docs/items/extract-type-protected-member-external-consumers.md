# extract-type-protected-member-external-consumers — Refuse or rebind extraction of protected members used by derived classes in other files/projects

**row:** `extract-type-protected-member-external-consumers` · **pri:** `Medium` · **size:** `S` · **deps:** `type-extraction-service-refusals-public-message`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (CollectExternalConsumerWarningsAsync)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [x] Shipped (PR #2064): a protected-member consumer in a DEPENDENT project is refused before token storage with a public message listing the CS ids and file:line.
- [ ] Remaining: same-project consumers (other documents) are refused with a structured blocking dependency (`blockingDependencies`) or rebound; red-first test. Today they already get the public CS-id PublicInvalidOperationException from RefuseAddedCompilerErrorsAsync, so a structured refusal replaces an existing public message (major); BlockingDependencyDto (Member, Reason) has no file/line fields either (DTO change).

## Evidence

- extract-type-override-modifier-stripped cold review: external-consumer scan skips private/protected members.

## Notes

2026-11-11 remediation: PR #2064 fixed the shared root cause for dependent projects (previously a token was issued; now refused with the CS ids). Remaining half = structured refusal for same-project consumers: major. Next-major trigger: ships with type-extraction-service-refusals-public-message (ADR + migration note per docs/release-policy.md Compatibility Policy), or an operator decision that replacing one refusal with another is not breaking.
