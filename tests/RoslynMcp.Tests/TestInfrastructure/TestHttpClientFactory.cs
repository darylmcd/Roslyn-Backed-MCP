namespace RoslynMcp.Tests;

internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler? _handler;

    public TestHttpClientFactory()
    {
    }

    public TestHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
        => _handler is null
            ? new HttpClient(new OfflineHandler())
            : new HttpClient(_handler, disposeHandler: false);

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
