# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.10.x  | Yes       |
| < 0.10  | No        |

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please use one of the following channels:

1. **GitHub Security Advisories** (preferred): Use the [Report a vulnerability](https://github.com/darylmcd/Roslyn-Backed-MCP/security/advisories/new) feature on this repository.
2. **Email**: Contact the maintainer directly via the email associated with the GitHub account [@darylmcd](https://github.com/darylmcd).

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

The server validates file paths against MCP client roots (when advertised) and resolves symlinks/junctions to prevent traversal attacks. However, path validation is a defense-in-depth measure and does not replace trust in the workspace content itself.
