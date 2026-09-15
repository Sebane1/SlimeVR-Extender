using System.Runtime.InteropServices;
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
    public const string DefaultRepository = "Sebane1/SlimeVR-Extender";

    private readonly HttpClient _httpClient;
    private readonly PlatformService _platformService;

    public ReleaseService(PlatformService platformService)
    {
        _platformService = platformService;

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SlimeVR-Extender/1.0 (C# App)"
        );
    }

    public async Task<GitHubRelease?> FetchLatestReleaseAsync(
        string repoOwnerAndName = DefaultRepository)
    {
        string url =
            $"https://api.github.com/repos/{repoOwnerAndName}/releases/latest";

        try
        {
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {

                url =
                    $"https://api.github.com/repos/{repoOwnerAndName}/releases";

                response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                string jsonArray =
                    await response.Content.ReadAsStringAsync();

                var releases =
                    JsonSerializer.Deserialize<List<GitHubRelease>>(jsonArray);

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

    /// <summary>
    /// Returns the packaged SlimeVR Server build for the current OS.
    ///
    public GitHubReleaseAsset? GetMatchingPlatformAsset(
        GitHubRelease release)
    {
        string? expectedName = GetServerAssetName();

        if (string.IsNullOrEmpty(expectedName))
            return null;

        return release.assets.FirstOrDefault(
            a => string.Equals(
                a.name,
                expectedName,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    /// <summary>
    /// Returns the SlimeVR Extender application's own update package.
    /// </summary>
    public GitHubReleaseAsset? GetMatchingExtenderAppAsset(
        GitHubRelease release)
    {
        string? expectedName = GetExtenderAssetName();

        if (!string.IsNullOrEmpty(expectedName))
        {
            var exactMatch = release.assets.FirstOrDefault(
                a => string.Equals(
                    a.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (exactMatch != null)
                return exactMatch;
        }

        // Fallback for future archive naming changes.
        string keyword =
            _platformService.GetReleaseAssetKeyword().ToLowerInvariant();

        return release.assets.FirstOrDefault(a =>
        {
            string name = a.name.ToLowerInvariant();

            return name.StartsWith("slimevr-extender-app-") &&
                   name.Contains(keyword);
        });
    }

    public GitHubReleaseAsset? GetMatchingDriverAsset(
        GitHubRelease release)
    {
        string driverZip = _platformService.GetDriverZipName();

        if (string.IsNullOrEmpty(driverZip))
            return null;

        // Prefer the exact known driver filename.
        var exactMatch = release.assets.FirstOrDefault(
            a => string.Equals(
                a.name,
                driverZip,
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (exactMatch != null)
            return exactMatch;

        // Allow packaged releases to rename the driver slightly.
        return release.assets.FirstOrDefault(
            a => a.name.Contains(
                "driver",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    private static string? GetServerAssetName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "slimevr-server-windows.zip";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.OSArchitecture ==
                   Architecture.Arm64
                ? "slimevr-server-linux-aarch64.tar.gz"
                : "slimevr-server-linux-x64.tar.gz";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "slimevr-server-macos.zip";

        return null;
    }

    private static string? GetExtenderAssetName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "slimevr-extender-app-windows-x64.zip";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.OSArchitecture ==
                   Architecture.Arm64
                ? "slimevr-extender-app-linux-aarch64.tar.gz"
                : "slimevr-extender-app-linux-x64.tar.gz";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "slimevr-extender-app-macos.zip";

        return null;
    }

    public async Task<string> DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        long totalBytes =
            response.Content.Headers.ContentLength ?? -1L;

        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            8192,
            true
        );

        byte[] buffer = new byte[16384];
        long totalRead = 0;

        while (true)
        {
            int bytesRead = await contentStream.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken
            );

            if (bytesRead == 0)
                break;

            await fileStream.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken
            );

            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                progress.Report(
                    (double)totalRead / totalBytes * 100.0
                );
            }
        }

        return destinationPath;
    }
}