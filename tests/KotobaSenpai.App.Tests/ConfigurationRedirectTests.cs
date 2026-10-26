using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.App.Tests;

public sealed class ConfigurationRedirectTests
{
    [Fact]
    public async Task Production_http_client_does_not_forward_credentials_to_a_redirect_destination()
    {
        var source = new TcpListener(IPAddress.Loopback, 0);
        var destination = new TcpListener(IPAddress.Loopback, 0);
        source.Start();
        destination.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var destinationPort = ((IPEndPoint)destination.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var connection = await source.AcceptTcpClientAsync(cancellation.Token);
                using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var headers = new List<string>();
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellation.Token)))
                    headers.Add(line);
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{destinationPort}/other\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, cancellation.Token);
                return headers;
            }, cancellation.Token);
            using var http = LlmHttpClient.Create();
            var sourcePort = ((IPEndPoint)source.LocalEndpoint).Port;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{sourcePort}/api");
            new OpenAiResponsesProtocol().ApplyAuthentication(request, "test-key");
            using var result = await http.SendAsync(request, cancellation.Token);
            Assert.Equal(HttpStatusCode.Found, result.StatusCode);
            Assert.Contains("Authorization: Bearer test-key", await server);
            Assert.False(destination.Pending());
        }
        finally { source.Stop(); destination.Stop(); }
    }
}
