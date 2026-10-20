namespace KotobaSenpai.Platform.Windows.Llm;

internal static class LlmEndpointHelper
{
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

        var baseText = baseUri.ToString().TrimEnd('/');
        var path = baseUri.AbsolutePath.TrimEnd('/');
        var candidates = new List<string>();
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"/v\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            candidates.Add(baseText + "/models");
        else
            candidates.Add(baseText + "/v1/models");

        var suffixes = new[] { "/api/anthropic", "/anthropic", "/claudecode", "/api/coding" };
        foreach (var suffix in suffixes)
        {
            if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            var root = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            var parentPath = path[..^suffix.Length].TrimEnd('/');
            if (parentPath.Length > 0)
                root += parentPath;
            candidates.Add(System.Text.RegularExpressions.Regex.IsMatch(parentPath, @"/v\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ? root + "/models"
                : root + "/v1/models");
            candidates.Add(root + "/models");
            break;
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new Uri(value, UriKind.Absolute))
            .ToArray();
    }
}
