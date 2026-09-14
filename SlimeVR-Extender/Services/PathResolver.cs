using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace SlimeVRExtender.Services;

public class PathResolver
{
    private readonly PlatformService _platformService;

    public PathResolver(PlatformService platformService)
    {
        _platformService = platformService;
    }

    public string DetectSteamPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string steamPath && Directory.Exists(steamPath))
                {
                    return steamPath;
                }
            }
            catch { }

            string programFilesSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            if (Directory.Exists(programFilesSteam)) return programFilesSteam;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] possiblePaths =
            [
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam")
            ];

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path)) return path;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string macSteam = Path.Combine(home, "Library", "Application Support", "Steam");
            if (Directory.Exists(macSteam)) return macSteam;
        }

        return string.Empty;
    }

    public string DetectSteamSlimeVRPath()
    {
        string steamPath = DetectSteamPath();
        if (string.IsNullOrEmpty(steamPath)) return string.Empty;

        string steamAppPath = Path.Combine(steamPath, "steamapps", "common", "SlimeVR");
        if (Directory.Exists(steamAppPath)) return steamAppPath;

        // Check alternate library paths from libraryfolders.vdf if available
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                foreach (string line in File.ReadAllLines(vdfPath))
                {
                    if (line.Contains("\"path\""))
                    {
                        var parts = line.Split('"');
                        if (parts.Length >= 4)
                        {
                            string libFolder = parts[3].Replace(@"\\", @"\");
                            string candidate = Path.Combine(libFolder, "steamapps", "common", "SlimeVR");
                            if (Directory.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
            catch { }
        }

        return steamAppPath;
    }

    public string DetectStandaloneSlimeVRPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string candidate = Path.Combine(localAppData, "Programs", "SlimeVR");
            if (Directory.Exists(candidate)) return candidate;

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string candidatePf = Path.Combine(programFiles, "SlimeVR");
            if (Directory.Exists(candidatePf)) return candidatePf;

            return candidate;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string candidate = Path.Combine(home, ".local", "share", "SlimeVR");
            if (Directory.Exists(candidate)) return candidate;
            return Path.Combine("/opt", "SlimeVR");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "/Applications/SlimeVR.app";
        }

        return string.Empty;
    }

    public string DetectSteamVRDriverPath()
    {
        // 1. Try reading openvrpaths.vrpath JSON
        string vrPathFile = GetOpenVRPathsFile();
        if (File.Exists(vrPathFile))
        {
            try
            {
                string content = File.ReadAllText(vrPathFile);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("external_drivers", out var driversElem) && driversElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in driversElem.EnumerateArray())
                    {
                        string path = elem.GetString() ?? "";
                        if (path.EndsWith("slimevr", StringComparison.OrdinalIgnoreCase) || path.EndsWith("driver_slimevr", StringComparison.OrdinalIgnoreCase))
                        {
                            if (Directory.Exists(path)) return path;
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Fallback to standard SteamVR drivers directory
        string steamPath = DetectSteamPath();
        if (!string.IsNullOrEmpty(steamPath))
        {
            string candidate = Path.Combine(steamPath, "steamapps", "common", "SteamVR", "drivers", "slimevr");
            return candidate;
        }

        return string.Empty;
    }

    private string GetOpenVRPathsFile()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "openvr", "openvrpaths.vrpath");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "openvr", "openvrpaths.vrpath");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "OpenVR", "openvrpaths.vrpath");
        }

        return string.Empty;
    }
}
