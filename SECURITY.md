# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 4.3.x   | Yes       |
| < 4.3   | No        |

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please use one of the following channels:

1. **GitHub Security Advisories** (preferred): Use the [Report a vulnerability](https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new) feature on this repository.
2. **Email**: Contact the maintainer directly via the email associated with the GitHub account [@darylmcd](https://github.com/darylmcd).

The shipped `/mcp-server-surface-test` skill enforces this policy automatically: any finding with `severity == P0` OR `area == security` is **refused for public filing** by both `--auto-file` (consumer) and `/backlog-intake --publish` (maintainer). Refused findings print to stdout with a banner pointing back here so the operator can route them through the channels above.

### What to include

- A description of the vulnerability and its potential impact.
- Steps to reproduce the issue or a proof-of-concept.
- The version(s) affected.
- Any suggested fix or mitigation, if known.

### Response timeline

- **Acknowledgement**: Within 3 business days of receipt.
- **Initial assessment**: Within 7 business days.
- **Fix or mitigation**: Depends on severity; critical issues are prioritized for immediate patch releases.

## Known Security Considerations

### MSBuild Evaluation

Loading a `.sln` or `.csproj` file triggers MSBuild evaluation, which can execute arbitrary build targets and tasks. **Only load solutions from trusted sources.** For untrusted code analysis, run the server in an isolated environment (container, VM, or sandbox).

### Path Validation

The server owns its path boundary. `ROSLYNMCP_SANCTIONED_ROOTS` (`SecurityOptions.SanctionedRoots`) configures the sanctioned roots that every path tool validates against; an empty boundary fails closed unless `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true` is set as an explicit, temporary compatibility escape hatch. Client-advertised or request-provided roots can only narrow the configured boundary and never widen it. Paths and roots are canonicalized component by component, resolving every existing symlink or junction in the ancestor chain, to prevent traversal. Sibling-worktree widening needs both the server operator opt-in (`ROSLYNMCP_ALLOW_ROOT_EXPANSION=true`) and a per-request opt-in. See [ADR 0002](docs/decisions/0002-configured-sanctioned-root-boundary.md). Path validation is a defense-in-depth measure and does not replace trust in the workspace content itself.
