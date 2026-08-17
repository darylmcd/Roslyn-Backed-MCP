# generated-json-schema-matcher-fail-closed — Make wire schema validation fail closed

**row:** `generated-json-schema-matcher-fail-closed` · **pri:** `Medium` · **size:** `S` · **deps:** `host-assembly-marker-wire-test-migration`

## Anchors

- `tests/RoslynMcp.Tests/Helpers/GeneratedJsonSchemaMatcher.cs`
- `tests/RoslynMcp.Tests/StructuredContentWireContractTests.cs`
- New `tests/RoslynMcp.Tests/GeneratedJsonSchemaMatcherTests.cs`.

## Acceptance

- [ ] Replace the custom matcher with an authoritative validator or explicitly inventory supported 2020-12 assertion versus annotation keywords.
- [ ] Reject/fail loudly on any unsupported assertion keyword while retaining fail-closed local `$ref` behavior.
- [ ] A sentinel schema/value proves a formerly ignored constraint cannot pass; annotation-only keywords remain documented.
- [ ] The all-eight dual-era wire matrix remains green against the actual advertised schemas.

## Evidence

- The current exporter schemas use assertion keywords the helper handles; `format` is annotation-only under the default 2020-12 vocabulary. The helper nevertheless returns true for an ignored assertion such as `{type:"string",minLength:5}` with `"x"`, so a future DTO constraint could false-green the public contract gate.
