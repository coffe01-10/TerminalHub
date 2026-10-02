using System.Net.Http;
using System.Text.Json;

namespace TerminalHub.Core.Settings;

public enum UpdatePlatform { Windows, Linux, Other }
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string Label);
public sealed record DownloadProgress(long Received, long? Total)
{
    public double Percent => Total is > 0 ? Math.Min(100, Received * 100.0 / Total.Value) : 0;
}
public sealed record AvailableRelease(string Tag, Version Version, Uri DownloadPage)
{
    public string Notes { get; init; } = "";
    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = [];
    public IEnumerable<ReleaseAsset> ForPlatform(UpdatePlatform platform) => Assets.Where(asset => platform switch
    {
        UpdatePlatform.Windows => asset.Label is "Windows 安装版" or "Windows 便携版",
        UpdatePlatform.Linux => asset.Label == "Linux x64 包",
        _ => false
    });
}

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
        var assets = new List<ReleaseAsset>();
        if (json.RootElement.TryGetProperty("assets", out var items))
            foreach (var item in items.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString()!;
                var label = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "Windows 安装版"
                    : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "Windows 便携版"
                    : name.Contains("linux-x64", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? "Linux x64 包" : "";
                if (label.Length > 0)
                    assets.Add(new(name, new Uri(item.GetProperty("browser_download_url").GetString()!), item.GetProperty("size").GetInt64(), label));
            }
        return new(tag, Version.Parse(tag.TrimStart('v')), new Uri(ReleasesUrl + "/tag/" + Uri.EscapeDataString(tag)))
        {
            Notes = json.RootElement.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "",
            Assets = assets
        };
    }

    public async Task DownloadAsync(ReleaseAsset asset, string destination, IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var partial = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, Path.GetRandomFileName());
        try
        {
            using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : (long?)null);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(new(received, total));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, destination, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
