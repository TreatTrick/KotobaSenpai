using System.Net.Http;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Shared provider transport policy for recognition and configuration entry points.</summary>
public static class LlmHttpClient
{
    public static HttpClient Create() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30) };
}
