# ADR 0026: Preserve struct construction paths during signature edits

Status: Accepted 2026-10-15

`change_signature_preview` must distinguish calls bound to the edited constructor from runtime-zero construction. Emitting an explicit parameterless constructor when the last struct parameter is removed changes serializers, generic activation, and callers outside the loaded solution. Rewriting visible zero-construction consumers cannot preserve those external paths.

When removing the last parameter of a primary or ordinary struct constructor, emit a fresh public nested marker type and a constructor with one required marker parameter. Rewrite calls bound to the selected constructor to pass an explicitly typed `default` marker. Leave other zero/default constructions and their initializers unchanged. The marker has no fields, defaults, or initialization effects. Its name must avoid all partial declarations and type members.

When adding a parameter to an explicit parameterless struct constructor, retain that constructor as the initialization owner. Add an edited entry point that delegates to it with `this()`, keeping its original initializer, body, and attributes in place. Preserve original partial declaration/implementation pairs and generate the corresponding added pair. Runtime attributes remain on the original owner; only compiler/analyzer call contracts are forwarded to the new entry point, avoiding duplicate constructor selection by serializers, DI containers, and custom consumers. An external constructor keeps its original ABI; the added entry point is managed and delegates to it.

Freeze compiler-supplied caller values in documents containing signature edits. Generated syntax and parameter-list changes must not silently change caller-line or caller-expression values at unrelated retained calls.

Reject a last-parameter removal when a known `JsonConstructor` or `ActivatorUtilitiesConstructor` attribute cannot preserve its parameter binding through a marker. Retained captures, overloads, grammar, and constructor chains must also pass candidate compilation and binding verification before a token is issued.

This changes the output of an experimental refactoring, with no new wire fields or stable category changes. It does not promise compatibility for reflection that explicitly enumerates the old constructor signature; changing that signature is the requested operation.

For migration, inspect the generated marker constructor in the preview instead of assuming last-parameter removal produces a literal parameterless constructor. Keep the generated marker argument at edited calls. Existing default construction and default serializer activation require no consumer rewrites.
