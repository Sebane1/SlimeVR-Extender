using System.Text.Json;

namespace SlimeVRExtender.Services;

public class GitHubReleaseAsset
{
    public string name { get; set; } = string.Empty;
    public string browser_download_url { get; set; } = string.Empty;
    public long size { get; set; }
}

public class GitHubRelease
{
    public string tag_name { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string body { get; set; } = string.Empty;
    public List<GitHubReleaseAsset> assets { get; set; } = new();
}

public class ReleaseService
{
    private readonly HttpClient _httpClient;
    private readonly PlatformService _platformService;

    public ReleaseService(PlatformService platformService)
    {
        _platformService = platformService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SlimeVR-Extender/1.0 (C# App)");
    }

    public async Task<GitHubRelease?> FetchLatestReleaseAsync(string repoOwnerAndName = "Sebane1/SlimeVR-Server")
    {
        string url = $"https://api.github.com/repos/{repoOwnerAndName}/releases/latest";
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                // Fallback to list releases if /latest fails (e.g. if pre-releases or workflow tags are used)
                url = $"https://api.github.com/repos/{repoOwnerAndName}/releases";
                response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                string jsonArray = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(jsonArray);
                return releases?.FirstOrDefault();
            }

            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitHubRelease>(json);
        }
        catch
        {
            return null;
        }
    }

    public GitHubReleaseAsset? GetMatchingPlatformAsset(GitHubRelease release)
    {
        string keyword = _platformService.GetReleaseAssetKeyword().ToLower();
        return release.assets.FirstOrDefault(a => a.name.ToLower().Contains(keyword));
    }

    public GitHubReleaseAsset? GetMatchingDriverAsset(GitHubRelease release)
    {
        string driverZip = _platformService.GetDriverZipName().ToLower();
        if (string.IsNullOrEmpty(driverZip)) return null;

        return release.assets.FirstOrDefault(a => a.name.ToLower().Contains("driver") || a.name.ToLower() == driverZip);
    }

    public GitHubReleaseAsset? GetMatchingExtenderAppAsset(GitHubRelease release)
    {
        string keyword = _platformService.GetReleaseAssetKeyword().ToLower();
        return release.assets.FirstOrDefault(a => a.name.ToLower().StartsWith("slimevr-extender") && a.name.ToLower().Contains(keyword));
    }

    public async Task<string> DownloadFileAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[16384];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                progress.Report((double)totalRead / totalBytes * 100.0);
            }
        }

        return destinationPath;
    }
}
