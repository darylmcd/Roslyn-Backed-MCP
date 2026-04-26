// Tests for the StdoutWriteAnalyzer (RMCP010). See
// analyzers/ServerSurfaceCatalogAnalyzer/StdoutWriteAnalyzer.cs for the analyzer
// implementation; these tests exercise the scenarios documented in
// ai_docs/plans/20260426T025255Z_backlog-sweep/plan.md initiative #1
// (stdio-host-stdout-audit):
//
//   (a) current Program.cs contents (Console.Out.Flush() + Console.Error.WriteLine
//       in ReadEnv) pass — analyzer emits zero diagnostics
//   (b) a synthetic Console.WriteLine("test") fails with RMCP010
//   (c) Console.Out.WriteLine, Console.Out.Write, Trace.WriteLine all flagged
//   (d) Console.Error.* calls pass (stderr is the canonical diagnostic channel)
//   (e) Aliased `var stderr = Console.Error; stderr.WriteLine(...)` passes
//   (f) Other-assembly compilations (assembly name != RoslynMcp.Host.Stdio) are no-ops
//
// The analyzer is assembly-scoped to RoslynMcp.Host.Stdio, so each test that needs
// the analyzer to fire transforms the test project's AssemblyName via SolutionTransforms.
// The "no-op outside Host.Stdio" scenario verifies the assembly-name guard.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Analyzers.ServerSurfaceCatalog;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class StdoutWriteAnalyzerTests
{
    private const string HostStdioAssemblyName = "RoslynMcp.Host.Stdio";

    [TestMethod]
    public async Task NoDiagnostics_ForCurrentProgramContents_OnlyFlushAndStderrWrites()
    {
        // Mirrors the actual Program.cs surface circa 2026-04-26: the only stdout-adjacent
        // calls are Console.Out.Flush() (protocol framing) and Console.Out.FlushAsync(); all
        // diagnostic writes go through Console.Error.WriteLine. Analyzer must emit zero
        // diagnostics on this shape — that is the entire premise of the audit.
        var source = """
            using System;

            namespace RoslynMcp.Host.Stdio
            {
                public static class ProgramSurrogate
                {
                    public static void Run()
                    {
                        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                        {
                            try { Console.Out.Flush(); } catch { }
                        };

                        Console.Out.Flush();
                        Console.Out.FlushAsync().GetAwaiter().GetResult();

                        // ReadEnv-style diagnostic logging routes through stderr.
                        Console.Error.WriteLine("[roslyn-mcp] Ignoring unresolved env placeholder.");
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task RMCP010_WhenConsoleWriteLineCalled()
    {
        // The headline scenario from the plan: a synthetic Console.WriteLine("test")
        // must produce RMCP010. This is the regression we want to lock down — if a
        // future refactor introduces a Console.WriteLine in Program.cs, build fails.
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class Leak
                {
                    public static void Bad()
                    {
                        {|#0:System.Console.WriteLine("test")|};
                    }
                }
            }
            """;

        var expected = new DiagnosticResult("RMCP010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Console.WriteLine", HostStdioAssemblyName);

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task RMCP010_WhenConsoleWriteCalled()
    {
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class Leak
                {
                    public static void Bad()
                    {
                        {|#0:System.Console.Write("test")|};
                    }
                }
            }
            """;

        var expected = new DiagnosticResult("RMCP010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Console.Write", HostStdioAssemblyName);

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task RMCP010_WhenConsoleOutWriteLineCalled()
    {
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class Leak
                {
                    public static void Bad()
                    {
                        {|#0:System.Console.Out.WriteLine("test")|};
                    }
                }
            }
            """;

        var expected = new DiagnosticResult("RMCP010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("System.Console.Out.WriteLine", HostStdioAssemblyName);

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task RMCP010_WhenTraceWriteLineCalled()
    {
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class Leak
                {
                    public static void Bad()
                    {
                        {|#0:System.Diagnostics.Trace.WriteLine("test")|};
                    }
                }
            }
            """;

        var expected = new DiagnosticResult("RMCP010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Trace.WriteLine", HostStdioAssemblyName);

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task NoDiagnostics_WhenConsoleErrorWriteCalled()
    {
        // Console.Error is the canonical diagnostic channel for stdio MCP servers.
        // Allow-listed unconditionally — every Write/WriteLine/WriteAsync overload is fine.
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class Diagnostics
                {
                    public static void Log()
                    {
                        System.Console.Error.WriteLine("informational");
                        System.Console.Error.Write("partial");
                        System.Console.Error.WriteLineAsync("async-line").GetAwaiter().GetResult();
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task NoDiagnostics_WhenConsoleOutFlushCalled()
    {
        // Flush() / FlushAsync() are protocol-required for the stdio NDJSON framing —
        // the host's Program.cs flushes on every exit path, and the analyzer must allow
        // that pattern even though Flush is technically a TextWriter.* member.
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class Framing
                {
                    public static void Flush()
                    {
                        System.Console.Out.Flush();
                        System.Console.Out.FlushAsync().GetAwaiter().GetResult();
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task NoDiagnostics_WhenStderrAliasWriteCalled()
    {
        // Aliased `var stderr = Console.Error; stderr.WriteLine(...)` is the same channel —
        // the analyzer follows the local's initializer to confirm it points at Console.Error.
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class AliasedStderr
                {
                    public static void Log()
                    {
                        var stderr = System.Console.Error;
                        stderr.WriteLine("via alias");
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [TestMethod]
    public async Task RMCP010_WhenStdoutAliasWriteCalled()
    {
        // The mirror case: `var stdout = Console.Out; stdout.WriteLine(...)` is a
        // pollution leak. The analyzer fires because the receiver is NOT Console.Error —
        // any TextWriter Write* call that isn't via Console.Error is forbidden inside Host.Stdio.
        var source = """
            namespace RoslynMcp.Host.Stdio
            {
                public static class AliasedStdout
                {
                    public static void Bad()
                    {
                        var stdout = System.Console.Out;
                        {|#0:stdout.WriteLine("via alias")|};
                    }
                }
            }
            """;

        var expected = new DiagnosticResult("RMCP010", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("stdout.WriteLine", HostStdioAssemblyName);

        await VerifyAsync(source, expected);
    }

    [TestMethod]
    public async Task NoDiagnostics_WhenAssemblyIsNotHostStdio()
    {
        // Sanity: the analyzer is assembly-scoped. A library or test assembly with the
        // same offending Console.WriteLine call must NOT receive RMCP010 — only Host.Stdio
        // owns the stdout-is-protocol invariant. We verify by skipping the assembly-name
        // transform so the test compilation keeps the harness's default assembly name.
        var source = """
            namespace SomeOtherAssembly
            {
                public static class Fine
                {
                    public static void Log()
                    {
                        System.Console.WriteLine("library code, no constraint");
                    }
                }
            }
            """;

        // Note: no SolutionTransforms call here — the test compilation's assembly name
        // is the harness default ("TestProject"), which is intentional for this case.
        var test = new CSharpAnalyzerTest<StdoutWriteAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        await test.RunAsync();
    }

    private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<StdoutWriteAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        // Rename the test compilation's assembly to RoslynMcp.Host.Stdio so the analyzer's
        // assembly-scope guard fires. Without this, every RMCP010-positive test would silently
        // pass because the analyzer skips compilations with non-Host.Stdio assembly names.
        test.SolutionTransforms.Add((solution, projectId) =>
            solution.WithProjectAssemblyName(projectId, HostStdioAssemblyName));

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
