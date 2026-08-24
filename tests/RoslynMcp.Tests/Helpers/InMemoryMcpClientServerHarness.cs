using System.IO.Pipelines;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tests.Helpers;

internal sealed record InMemoryMcpHarnessOptions(
    string TransportName,
    ClientCapabilities ClientCapabilities,
    McpClientHandlers ClientHandlers,
    string DisposalFailureContext)
{
    public string? ProtocolVersion { get; init; }
    public Func<ServiceProvider>? ServerServicesFactory { get; init; }
    public McpServerOptions? ServerOptions { get; init; }
    public bool CaptureServerMessages { get; init; }
    public Func<Stream, Stream, IHost>? ServerHostFactory { get; init; }
}

/// <summary>
/// Hosts a real MCP client/server pair over in-memory duplex pipes for integration tests that
/// need connection-scoped client capabilities and request handlers.
/// </summary>
/// <remarks>
/// <para><b>Disposal invariant — every client request handler this harness is given MUST be
/// completable, and MUST be completed before <see cref="DisposeAsync"/> runs.</b>
/// <see cref="DisposeOwnedResourcesAsync"/> disposes <see cref="Client"/> first (line ~155), and
/// <c>McpClient.DisposeAsync</c> waits for outstanding inbound request handlers to finish. A
/// handler that returns a never-completing task (e.g.
/// <c>new TaskCompletionSource&lt;ElicitResult&gt;().Task</c>) therefore wedges teardown
/// <i>forever</i> once the request actually reaches it — and because that hang is in
/// <c>await using</c> teardown rather than in the test body, MSTest's non-cooperative
/// <c>[Timeout]</c> backstop cannot recover it either; the whole test process has to be killed.</para>
///
/// <para><b>This was the root cause of the long-standing
/// <c>elicitation-inflight-cancellation-test-harness-deadlock</c> dead end</b>, isolated
/// 2026-08-13 with a standalone out-of-process repro of this exact harness. Findings, so a future
/// attempt does not re-walk it:
/// <list type="bullet">
///   <item><c>server.ElicitAsync(request, token)</c> is <b>not</b> the culprit and never was. With
///         the client permanently unresponsive, it observed cancellation and threw
///         <c>TaskCanceledException</c> <b>~2 ms</b> after <c>Cancel()</c> in all three in-flight
///         cancel shapes previously tried (inline from the client handler, decoupled via
///         <c>Task.Run</c>, and from a dedicated OS thread). No <c>.AsTask().WaitAsync(token)</c>
///         wrapper is needed — which is why adding one never helped.</item>
///   <item>ThreadPool starvation: <b>ruled out</b>. At the hang point 32767 of 32767 worker threads
///         and 1000 of 1000 IO threads were available (<c>ThreadPool.ThreadCount</c> 4), so
///         <c>[DoNotParallelize]</c> and single-process hosting are irrelevant here.</item>
///   <item><c>Pipe</c> backpressure / missing reader: <b>ruled out</b>. Both directions kept
///         flowing (bytes written == bytes read, one pending read outstanding per reader, which is
///         the healthy idle shape).</item>
///   <item>The discriminator is solely <i>whether the client handler completes</i>: a handler that
///         returns a result disposes cleanly in ~6 ms; an entered handler that never completes
///         hangs indefinitely. A request that never reaches the handler (the pre-cancelled-token
///         case) also disposes cleanly, because nothing is outstanding.</item>
/// </list></para>
///
/// <para>Consequence: pass a <see cref="TaskCompletionSource{TResult}"/>-backed handler the test
/// owns and complete it before disposal (see
/// <c>SymbolDisambiguationElicitationTests.ControllableElicitationHarness</c>, which enforces the
/// ordering in its own <c>DisposeAsync</c>) rather than a handler that can never complete.</para>
/// </remarks>
internal sealed class InMemoryMcpClientServerHarness : IAsyncDisposable
{
    private const string _legacyConnectionScopedProtocolVersion = "2025-11-25";
    private readonly ServerRuntime _serverRuntime;
    private readonly IReadOnlyList<Stream> _transportStreams;
    private readonly Func<IReadOnlyList<string>>? _serverMessageSnapshot;
    private readonly string _disposalFailureContext;

    private InMemoryMcpClientServerHarness(
        ServerRuntime serverRuntime,
        McpClient client,
        IReadOnlyList<Stream> transportStreams,
        Func<IReadOnlyList<string>>? serverMessageSnapshot,
        string disposalFailureContext)
    {
        _serverRuntime = serverRuntime;
        Client = client;
        _transportStreams = transportStreams;
        _serverMessageSnapshot = serverMessageSnapshot;
        _disposalFailureContext = disposalFailureContext;
    }

    public McpServer Server => _serverRuntime.Server;
    public McpClient Client { get; }

    public IReadOnlyList<string> RawServerMessages =>
        _serverMessageSnapshot?.Invoke()
        ?? throw new InvalidOperationException(
            "Raw server-message capture was not enabled for this harness.");

    public static Task<InMemoryMcpClientServerHarness> CreateAsync(
        string transportName,
        ClientCapabilities clientCapabilities,
        McpClientHandlers clientHandlers,
        string disposalFailureContext,
        CancellationToken cancellationToken,
        string? protocolVersion = _legacyConnectionScopedProtocolVersion,
        Func<ServiceProvider>? serverServicesFactory = null,
        McpServerOptions? serverOptions = null,
        bool captureServerMessages = false) =>
        CreateAsync(
            new InMemoryMcpHarnessOptions(
                transportName,
                clientCapabilities,
                clientHandlers,
                disposalFailureContext)
            {
                ProtocolVersion = protocolVersion,
                ServerServicesFactory = serverServicesFactory,
                ServerOptions = serverOptions,
                CaptureServerMessages = captureServerMessages,
            },
            cancellationToken);

    public static async Task<InMemoryMcpClientServerHarness> CreateAsync(
        InMemoryMcpHarnessOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.TransportName);
        ArgumentNullException.ThrowIfNull(options.ClientCapabilities);
        ArgumentNullException.ThrowIfNull(options.ClientHandlers);
        ArgumentException.ThrowIfNullOrEmpty(options.DisposalFailureContext);
        if (options.ServerHostFactory is not null &&
            (options.ServerServicesFactory is not null || options.ServerOptions is not null))
        {
            throw new ArgumentException(
                "A hosted server factory owns service composition and server options; do not supply either separately.",
                nameof(options));
        }

        ServerRuntime? serverRuntime = null;
        McpClient? client = null;
        var transportStreams = new List<Stream>(capacity: 4);
        var serverWireCapture = options.CaptureServerMessages ? new ServerWireCapture() : null;

        try
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var clientToServerReadStream = clientToServer.Reader.AsStream();
            transportStreams.Add(clientToServerReadStream);
            var clientToServerWriteStream = clientToServer.Writer.AsStream();
            transportStreams.Add(clientToServerWriteStream);
            var serverToClientReadStream = serverToClient.Reader.AsStream();
            transportStreams.Add(serverToClientReadStream);
            var serverToClientPipeStream = serverToClient.Writer.AsStream();
            Stream serverToClientWriteStream = serverWireCapture is null
                ? serverToClientPipeStream
                : new CapturingWriteStream(serverToClientPipeStream, serverWireCapture);
            transportStreams.Add(serverToClientWriteStream);

            serverRuntime = await CreateServerRuntimeAsync(
                options.TransportName,
                clientToServerReadStream,
                serverToClientWriteStream,
                options.ServerServicesFactory,
                options.ServerOptions,
                options.ServerHostFactory,
                cancellationToken).ConfigureAwait(false);

            var clientTransport = new StreamClientTransport(
                clientToServerWriteStream,
                serverToClientReadStream,
                NullLoggerFactory.Instance);
            client = await McpClient.CreateAsync(
                clientTransport,
                new McpClientOptions
                {
                    // These tests directly invoke the connection-scoped server after the initialize
                    // handshake. Protocol revisions through 2025-11-25 expose capabilities there;
                    // modern request-scoped behavior is covered separately through tools/call.
                    ProtocolVersion = options.ProtocolVersion,
                    Capabilities = options.ClientCapabilities,
                    Handlers = options.ClientHandlers,
                },
                NullLoggerFactory.Instance,
                cancellationToken).ConfigureAwait(false);

            Func<IReadOnlyList<string>>? serverMessageSnapshot = serverWireCapture is null
                ? null
                : serverWireCapture.Snapshot;

            return new InMemoryMcpClientServerHarness(
                serverRuntime,
                client,
                transportStreams,
                serverMessageSnapshot,
                options.DisposalFailureContext);
        }
        catch (Exception initializationFailure)
        {
            var cleanupFailures = await DisposeOwnedResourcesAsync(
                client,
                serverRuntime,
                transportStreams).ConfigureAwait(false);
            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, initializationFailure);
                throw new AggregateException(
                    $"Failed to initialize and dispose the {options.DisposalFailureContext} MCP test harness.",
                    cleanupFailures);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = await DisposeOwnedResourcesAsync(
            Client,
            _serverRuntime,
            _transportStreams).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Failed to dispose the {_disposalFailureContext} MCP test harness.",
                failures);
        }
    }

    private static async Task<ServerRuntime> CreateServerRuntimeAsync(
        string transportName,
        Stream serverInput,
        Stream serverOutput,
        Func<ServiceProvider>? serverServicesFactory,
        McpServerOptions? serverOptions,
        Func<Stream, Stream, IHost>? serverHostFactory,
        CancellationToken cancellationToken)
    {
        if (serverHostFactory is not null)
        {
            var host = serverHostFactory(serverInput, serverOutput);
            try
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
                return new ServerRuntime(
                    host.Services.GetRequiredService<McpServer>(),
                    Host: host);
            }
            catch (Exception initializationFailure)
            {
                var cleanupFailures = new List<Exception>();
                await StopAndDisposeHostAsync(host, cleanupFailures).ConfigureAwait(false);
                if (cleanupFailures.Count > 0)
                {
                    cleanupFailures.Insert(0, initializationFailure);
                    throw new AggregateException(
                        "Failed to initialize and dispose the hosted MCP test server.",
                        cleanupFailures);
                }

                throw;
            }
        }

        ServiceProvider? services = null;
        McpServer? server = null;
        CancellationTokenSource? cancellation = null;
        Task? runTask = null;
        try
        {
            services = serverServicesFactory?.Invoke();
            var transport = new StreamServerTransport(
                serverInput,
                serverOutput,
                transportName,
                NullLoggerFactory.Instance);
            server = McpServer.Create(
                transport,
                serverOptions ?? new McpServerOptions(),
                NullLoggerFactory.Instance,
                services);
            cancellation = new CancellationTokenSource();
            runTask = server.RunAsync(cancellation.Token);
            return new ServerRuntime(
                server,
                Cancellation: cancellation,
                RunTask: runTask,
                Services: services);
        }
        catch (Exception initializationFailure)
        {
            var cleanupFailures = new List<Exception>();
            if (server is not null)
            {
                await StopDirectServerAsync(
                    new ServerRuntime(server, cancellation, runTask),
                    cleanupFailures).ConfigureAwait(false);
            }

            if (services is not null)
            {
                await DisposeCapturingAsync(services, cleanupFailures).ConfigureAwait(false);
            }

            cancellation?.Dispose();

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, initializationFailure);
                throw new AggregateException(
                    "Failed to initialize and dispose the direct MCP test server.",
                    cleanupFailures);
            }

            throw;
        }
    }

    private static async ValueTask<List<Exception>> DisposeOwnedResourcesAsync(
        McpClient? client,
        ServerRuntime? serverRuntime,
        IReadOnlyList<Stream> transportStreams)
    {
        var failures = new List<Exception>();
        if (serverRuntime?.Host is not null)
        {
            // Hosted roots fixtures may have a legacy server-to-client Roots request in flight.
            // Stop the receive loop before disposing the client so teardown cannot wait forever.
            await StopHostAsync(serverRuntime.Host, failures).ConfigureAwait(false);
        }

        if (client is not null)
        {
            await DisposeCapturingAsync(client, failures).ConfigureAwait(false);
        }

        if (serverRuntime is not null && serverRuntime.Host is null)
        {
            await StopDirectServerAsync(serverRuntime, failures).ConfigureAwait(false);
        }

        foreach (var stream in transportStreams)
        {
            await DisposeCapturingAsync(stream, failures).ConfigureAwait(false);
        }

        if (serverRuntime?.Services is not null)
        {
            await DisposeCapturingAsync(serverRuntime.Services, failures).ConfigureAwait(false);
        }

        if (serverRuntime?.Host is IAsyncDisposable asyncHost)
        {
            await DisposeCapturingAsync(asyncHost, failures).ConfigureAwait(false);
        }
        else if (serverRuntime?.Host is not null)
        {
            Capture(() => serverRuntime.Host.Dispose(), failures);
        }

        serverRuntime?.Cancellation?.Dispose();
        return failures;
    }

    private static async ValueTask StopDirectServerAsync(
        ServerRuntime runtime,
        List<Exception> failures)
    {
        if (runtime.Cancellation is not null)
        {
            Capture(runtime.Cancellation.Cancel, failures);
        }

        if (runtime.RunTask is not null)
        {
            try
            {
                await runtime.RunTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runtime.Cancellation?.IsCancellationRequested == true)
            {
                // Expected — cancelling the server's receive loop surfaces as this.
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        await DisposeCapturingAsync(runtime.Server, failures).ConfigureAwait(false);
    }

    private static async ValueTask StopAndDisposeHostAsync(IHost host, List<Exception> failures)
    {
        await StopHostAsync(host, failures).ConfigureAwait(false);
        if (host is IAsyncDisposable asyncHost)
        {
            await DisposeCapturingAsync(asyncHost, failures).ConfigureAwait(false);
        }
        else
        {
            Capture(host.Dispose, failures);
        }
    }

    private static async ValueTask StopHostAsync(IHost host, List<Exception> failures)
    {
        try
        {
            await host.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    private static void Capture(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    private static async ValueTask DisposeCapturingAsync(
        IAsyncDisposable disposable,
        List<Exception> failures)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    private sealed record ServerRuntime(
        McpServer Server,
        CancellationTokenSource? Cancellation = null,
        Task? RunTask = null,
        ServiceProvider? Services = null,
        IHost? Host = null);

    private sealed class ServerWireCapture
    {
        private readonly Lock _lock = new();
        private readonly MemoryStream _buffer = new();

        public void Append(ReadOnlySpan<byte> bytes)
        {
            lock (_lock)
            {
                _buffer.Write(bytes);
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_lock)
            {
                return Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, checked((int)_buffer.Length))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
        }
    }

    private sealed class CapturingWriteStream(
        Stream inner,
        ServerWireCapture capture) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            capture.Append(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            capture.Append(buffer);
            inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            capture.Append(buffer.AsSpan(offset, count));
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            capture.Append(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
