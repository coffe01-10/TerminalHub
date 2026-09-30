using System.Net.Http;
using System.Text.Json;

namespace TerminalHub.Core.Settings;

public sealed record AvailableRelease(string Tag, Version Version, Uri DownloadPage);

public sealed class ReleaseUpdates
{
    public const string ReleasesUrl = "https://github.com/coffe01-10/TerminalHub/releases";
    private readonly HttpClient _http;
    public ReleaseUpdates(HttpClient http) => _http = http;
    public async Task<AvailableRelease> LatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/coffe01-10/TerminalHub/releases/latest");
        request.Headers.UserAgent.ParseAdd("TerminalHub/0.3");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = json.RootElement.GetProperty("tag_name").GetString()!;
        return new(tag, Version.Parse(tag.TrimStart('v')), new Uri(ReleasesUrl + "/tag/" + Uri.EscapeDataString(tag)));
    }
}
