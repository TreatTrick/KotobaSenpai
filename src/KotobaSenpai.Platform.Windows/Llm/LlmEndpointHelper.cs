namespace KotobaSenpai.Platform.Windows.Llm;

internal static class LlmEndpointHelper
{
    public static Uri ForProtocol(Uri endpoint, string path)
    {
        foreach (var suffix in new[] { "/chat/completions", "/responses", "/messages" })
        {
            var current = endpoint.AbsolutePath.TrimEnd('/');
            if (current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return new UriBuilder(endpoint) { Path = Join(current[..^suffix.Length], path) }.Uri;
        }
        return endpoint;
    }

    public static IReadOnlyList<Uri> AnalysisCandidates(Uri endpoint, string path)
    {
        var basePath = endpoint.AbsolutePath.TrimEnd('/');
        if (basePath.EndsWith(path, StringComparison.OrdinalIgnoreCase))
            basePath = basePath[..^path.Length];

        var versionedPath = path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) ? path : "/v1" + path;
        var candidates = new List<Uri>
        {
            endpoint,
            new UriBuilder(endpoint) { Path = Join(basePath, versionedPath) }.Uri,
            new UriBuilder(endpoint) { Path = Join(basePath, path) }.Uri,
        };

        if (path == "/v1/messages")
        {
            var prefix = basePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? basePath[..^3] : basePath;
            if (!prefix.EndsWith("/anthropic", StringComparison.OrdinalIgnoreCase))
                prefix += "/anthropic";
            candidates.Add(new UriBuilder(endpoint) { Path = Join(prefix, path) }.Uri);
        }

        return candidates.Distinct().ToArray();
    }

    public static string Join(string endpoint, string path)
    {
        var baseUrl = endpoint.Trim().TrimEnd('/');
        var relative = path.TrimStart('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            && relative.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
            relative = relative[3..];
        return $"{baseUrl}/{relative}";
    }

    public static IReadOnlyList<Uri> ModelCandidates(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            return [];

        var path = baseUri.AbsolutePath.TrimEnd('/');
        foreach (var analysisSuffix in new[] { "/chat/completions", "/responses", "/messages", "/models" })
            if (path.EndsWith(analysisSuffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^analysisSuffix.Length];
                break;
            }
        var candidates = new List<string>();
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"/v\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            candidates.Add(path + "/models");
        else
            candidates.Add(path + "/v1/models");

        var suffixes = new[] { "/api/anthropic", "/anthropic", "/claudecode", "/api/coding" };
        foreach (var suffix in suffixes)
        {
            if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            var parentPath = path[..^suffix.Length].TrimEnd('/');
            candidates.Add(System.Text.RegularExpressions.Regex.IsMatch(parentPath, @"/v\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? parentPath + "/models"
                : parentPath + "/v1/models");
            candidates.Add(parentPath + "/models");
            break;
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new UriBuilder(baseUri) { Path = value }.Uri)
            .ToArray();
    }
}
