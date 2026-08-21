# get-prompt-binding-stage-contract-adapter — Own the prompt binding-stage contract

**row:** `get-prompt-binding-stage-contract-adapter` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/GetPromptErrorFilter.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs`
- `tests/RoslynMcp.Tests/PromptCallErrorFilterTests.cs`

## Acceptance

- [ ] Replace private `AIFunctionFactory.ReflectionAIFunctionDescriptor` stack-frame recognition with an owned binding-stage adapter or explicit typed contract.
- [ ] Missing and malformed prompt arguments remain actionable `InvalidParams`; the same exception types and parameter names thrown by handlers remain sanitized `InternalError`.
- [ ] Wire tests fail closed when the pinned SDK changes its private implementation shape.

## Evidence

- MCP SDK 2.1.0 performs binding and handler invocation behind one filter callback and throws the same public exception types from both stages; the current bounded classifier must recognize a private SDK frame.
