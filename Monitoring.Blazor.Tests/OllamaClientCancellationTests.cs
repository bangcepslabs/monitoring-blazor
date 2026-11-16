using Microsoft.Extensions.Configuration;
using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class OllamaClientCancellationTests
{
    [Fact]
    public async Task GenerateAsync_CancelsInFlightOllamaRequest()
    {
        var handler = new WaitingHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monitoring:Ollama:Enabled"] = "true",
                ["Monitoring:Ollama:BaseUrl"] = "http://localhost:11434",
                ["Monitoring:Ollama:Model"] = "test-model"
            })
            .Build();
        var client = new OllamaClient(new TestHttpClientFactory(handler), configuration, new RuntimeSettingsRepository(configuration));
        using var cancellation = new CancellationTokenSource();

        var generation = client.GenerateAsync("test prompt", null, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation);
        Assert.True(handler.RequestCancelled);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RequestCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RequestCancelled = true;
                throw;
            }

            throw new InvalidOperationException("The request should have been cancelled.");
        }
    }
}
