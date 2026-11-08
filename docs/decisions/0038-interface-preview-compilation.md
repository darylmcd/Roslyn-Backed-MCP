# ADR 0038 — Compile the final interface preview

- **Status:** Accepted (2026-11-08)
- **Deciders:** repository maintainers
- **Scope:** cross-project interface extraction, dependency inversion and extract-and-wire previews
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

Reference closure and cyclic-refusal checks do not cover every compiler error an extracted interface can introduce. A source project can use metadata unavailable to the receiver, dependency inversion can replace a concrete parameter whose other members are used by a consumer, and extract-and-wire can rewrite a registration into an invalid generic call. The composite route previously assembled DI edits from disk text and could overwrite extraction edits in the same physical file.

## Decision

- Compare compiler errors in every changed project and its transitive dependents between the loaded baseline and the final pending solution. Match existing errors by project and document identity; reject only added errors. Include every distinct new compiler diagnostic id and affected project in the refusal before storing a preview token.
- Build extract-and-wire's final solution by composing registration changes on the extracted Roslyn solution. Reconcile linked documents by physical path on every route, add valid target references to every linked project that now compiles the interface base, and refuse cycles, divergent edits or unmapped changed C# files. Render one mutation and preview diff per physical file, before the diff-size cap, then run the compiler comparison before storing the composite token.
- Name the generated interface by its fully qualified symbol in rewritten DI registrations, so a registration in another file resolves it without an added `using` directive.
- Keep the generated interface's nullable and language checks. The compiler comparison supplements those checks.

## Migration

Callers receiving a compiler-error refusal should make the signature available to the receiving project, retain required concrete behavior at a consumer, or choose a valid registration call, then request a fresh preview. The public request and response shapes remain unchanged. A composite preview now includes both extraction and DI edits to one file.

## Verification

Regression tests cover missing source-only metadata, consumer member errors, invalid registration rewrites, same-file composition, linked project reference propagation and cycles, unmapped registration files and pre-existing compiler errors. Existing cross-project success assertions require compilation of all projects.
