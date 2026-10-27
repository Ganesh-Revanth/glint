using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Glint.Core.Security;
using Glint.Providers.Gemini;
using Xunit;

namespace Glint.Tests;

public class GeminiProviderTests
{
    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _keys = new();

        public void SaveApiKey(string provider, string apiKey) => _keys[provider] = apiKey;
        public string? GetApiKey(string provider) => _keys.TryGetValue(provider, out var k) ? k : null;
        public void RemoveApiKey(string provider) => _keys.Remove(provider);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task GenerateReplyAsync_ThrowsWhenApiKeyMissing()
    {
        var store = new InMemoryCredentialStore();
        var provider = new GeminiProvider(store);

        var dummyImage = new byte[] { 1, 2, 3 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GenerateReplyAsync("test", dummyImage));
    }

    [Fact]
    public async Task GenerateReplyAsync_ParsesSuccessfulGeminiResponse()
    {
        var store = new InMemoryCredentialStore();
        store.SaveApiKey(GeminiProvider.ProviderKey, "test-api-key");

        var mockJson = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "The screen shows a code editor with C#."
                  }
                ]
              }
            }
          ]
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("key=test-api-key", req.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var provider = new GeminiProvider(store, client);

        var reply = await provider.GenerateReplyAsync("What is this?", new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        Assert.Equal("The screen shows a code editor with C#.", reply);
    }

    [Fact]
    public async Task GenerateReplyAsync_ThrowsOnApiErrorResponse()
    {
        var store = new InMemoryCredentialStore();
        store.SaveApiKey(GeminiProvider.ProviderKey, "invalid-key");

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\": {\"message\": \"API_KEY_INVALID\"}}")
        });

        using var client = new HttpClient(handler);
        var provider = new GeminiProvider(store, client);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GenerateReplyAsync("test", new byte[] { 1, 2, 3 }));
    }
}
