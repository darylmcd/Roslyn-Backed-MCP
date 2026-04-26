---
category: Added
---

- **Added:** RMCP010 analyzer (`StdoutWriteAnalyzer`) that prevents `Console.Out.Write*` / `Trace.WriteLine` from leaking into `RoslynMcp.Host.Stdio` (would corrupt the stdio NDJSON framing). Allow-listed `Console.Out.Flush*` (protocol-required) and `Console.Error.*` (stderr-safe). Closes `stdio-host-stdout-audit`.
