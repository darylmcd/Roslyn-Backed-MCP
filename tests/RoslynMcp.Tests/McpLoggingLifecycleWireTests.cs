using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class McpLoggingLifecycleWireTests
{
    [TestMethod]
    [Timeout(30000)]
    public async Task ProductionHost_EmitsNoProtocolLoggingBeforeOrAfterInitialization()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var hostDll = typeof(HostAssemblyMarker).Assembly.Location;
        Assert.IsTrue(File.Exists(hostDll), $"Build the host before the wire test: {hostDll}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", $"\"{hostDll}\"")
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.Environment[ServerObservabilityOptions.EnvironmentVariableName] = "stderr";
        Assert.IsTrue(process.Start());

        var stdout = new ConcurrentQueue<string>();
        var stderr = new ConcurrentQueue<string>();
        var stdoutPump = PumpLinesAsync(process.StandardOutput, stdout);
        var stderrPump = PumpLinesAsync(process.StandardError, stderr);

        try
        {
            await WaitForLineAsync(stderr, "Startup surface:");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.IsEmpty(stdout, "The host emitted a protocol frame before initialize.");

            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-07-28","capabilities":{},"clientInfo":{"name":"wire-test","version":"1.0"}}}
                """);
            await WaitForResponseAsync(stdout, id: 1);
            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
                """);
            await WriteMessageAsync(
                process,
                """
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"server_info","arguments":{}}}
                """);
            var serverInfoResponse = await WaitForResponseAsync(stdout, id: 2);

            using var document = JsonDocument.Parse(serverInfoResponse);
            var text = document.RootElement
                .GetProperty("result")
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();
            Assert.IsNotNull(text);
            using var payload = JsonDocument.Parse(text);
            Assert.IsFalse(payload.RootElement
                .GetProperty("capabilities")
                .GetProperty("logging")
                .GetBoolean());

            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await StopProcessAsync(process);
            await Task.WhenAll(stdoutPump, stderrPump);
        }

        foreach (var line in stdout)
        {
            using var document = JsonDocument.Parse(line);
            Assert.IsFalse(
                document.RootElement.TryGetProperty("method", out var method) &&
                method.GetString() == "notifications/message",
                $"Protocol logging frame leaked onto stdout: {line}");
        }

        Assert.IsNotEmpty(stderr, "Operational host logs must remain available on stderr.");
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static async Task PumpLinesAsync(StreamReader reader, ConcurrentQueue<string> destination)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                destination.Enqueue(line);
            }
        }
    }

    private static async Task WriteMessageAsync(Process process, string json)
    {
        await process.StandardInput.WriteLineAsync(json);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<string> WaitForResponseAsync(
        ConcurrentQueue<string> messages,
        int id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var message in messages)
            {
                using var document = JsonDocument.Parse(message);
                if (document.RootElement.TryGetProperty("id", out var responseId) &&
                    responseId.ValueKind == JsonValueKind.Number &&
                    responseId.GetInt32() == id)
                {
                    return message;
                }
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Timed out waiting for MCP response id {id}. stdout={string.Join(" | ", messages)}");
        return string.Empty;
    }

    private static async Task WaitForLineAsync(
        ConcurrentQueue<string> lines,
        string expectedText)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (lines.Any(line => line.Contains(expectedText, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Timed out waiting for stderr marker '{expectedText}'. stderr={string.Join(" | ", lines)}");
    }
}
