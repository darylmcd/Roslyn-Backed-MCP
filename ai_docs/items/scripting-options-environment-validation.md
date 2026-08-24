# scripting-options-environment-validation — Validate script timing configuration at startup

**row:** `scripting-options-environment-validation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/Configuration/ScriptingOptionsEnvironmentBinder.cs` (new)
- `tests/RoslynMcp.Tests/ScriptingOptionsEnvironmentBinderTests.cs` (new)

## Acceptance

- [ ] Extract scripting environment parsing into a directly testable binder and validate every numeric variable with its documented lower bound.
- [ ] Validate timeout plus watchdog grace against the runtime timer limit once during host startup; invalid configuration never waits for the first tool call.
- [ ] Fail startup with the variable name and safe range for malformed, nonpositive, or incompatible values; never echo the raw environment value.
- [ ] Preserve unresolved `${user_config.KEY}` placeholders as unset/default input.
- [ ] One table-driven binder regression covers unset, placeholder, malformed, boundary, and timeout-plus-grace combinations.

## Evidence

`Program.BindScriptingServiceOptions` currently ignores malformed and nonpositive `ROSLYNMCP_SCRIPT_*` values, while oversized positive timeout/grace combinations fail only on the first evaluation. A configured-default failure is also reported today as `timeoutSecondsOverride` even when no override was supplied.
