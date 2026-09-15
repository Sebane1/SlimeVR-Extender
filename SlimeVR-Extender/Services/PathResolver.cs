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
                using var key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Valve\Steam"
                    );

                if (key?.GetValue("SteamPath") is string steamPath &&
                    Directory.Exists(steamPath))
                {
                    return steamPath;
                }
            }
            catch
            {
                // Fall through to normal installation locations.
            }

            string programFilesSteam = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86
                ),
                "Steam"
            );

            if (Directory.Exists(programFilesSteam))
                return programFilesSteam;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            string[] possiblePaths =
            [
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam")
            ];

            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path))
                    return path;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            string macSteam = Path.Combine(
                home,
                "Library",
                "Application Support",
                "Steam"
            );

            if (Directory.Exists(macSteam))
                return macSteam;
        }

        return string.Empty;
    }

    public string DetectSteamSlimeVRPath()
    {
        string steamPath = DetectSteamPath();

        if (string.IsNullOrWhiteSpace(steamPath))
            return string.Empty;

        string steamAppPath = Path.Combine(
            steamPath,
            "steamapps",
            "common",
            "SlimeVR"
        );

        if (IsValidSlimeVRDirectory(steamAppPath))
            return steamAppPath;

        string vdfPath = Path.Combine(
            steamPath,
            "steamapps",
            "libraryfolders.vdf"
        );

        if (File.Exists(vdfPath))
        {
            try
            {
                foreach (string line in File.ReadAllLines(vdfPath))
                {
                    if (!line.Contains(
                            "\"path\"",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string[] parts = line.Split('"');

                    if (parts.Length < 4)
                        continue;

                    string libraryFolder =
                        parts[3].Replace(@"\\", @"\");

                    string candidate = Path.Combine(
                        libraryFolder,
                        "steamapps",
                        "common",
                        "SlimeVR"
                    );

                    if (IsValidSlimeVRDirectory(candidate))
                        return candidate;
                }
            }
            catch
            {
                // A malformed VDF should not result in an invented path.
            }
        }
        return string.Empty;
    }

    public string DetectStandaloneSlimeVRPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var candidates = new List<string>();

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                );

            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86
                );

            string localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                );

            // Current Windows installer name.
            AddCandidate(
                candidates,
                programFiles,
                "SlimeVR Server"
            );

            AddCandidate(
                candidates,
                programFilesX86,
                "SlimeVR Server"
            );

            AddCandidate(
                candidates,
                localAppData,
                "Programs",
                "SlimeVR Server"
            );

            // Older / alternate installs.
            AddCandidate(
                candidates,
                programFiles,
                "SlimeVR"
            );

            AddCandidate(
                candidates,
                programFilesX86,
                "SlimeVR"
            );

            AddCandidate(
                candidates,
                localAppData,
                "Programs",
                "SlimeVR"
            );

            foreach (string candidate in candidates)
            {
                if (IsValidSlimeVRDirectory(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            string[] candidates =
            [
                Path.Combine(
                    home,
                    ".local",
                    "share",
                    "SlimeVR"
                ),
                "/opt/SlimeVR"
            ];

            foreach (string candidate in candidates)
            {
                if (IsValidSlimeVRDirectory(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            const string candidate = "/Applications/SlimeVR.app";

            return Directory.Exists(candidate)
                ? candidate
                : string.Empty;
        }

        return string.Empty;
    }

    public string DetectSteamVRDriverPath()
    {
        string vrPathFile = GetOpenVRPathsFile();

        if (File.Exists(vrPathFile))
        {
            try
            {
                string content =
                    File.ReadAllText(vrPathFile);

                using var doc =
                    JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty(
                        "external_drivers",
                        out var driversElem) &&
                    driversElem.ValueKind ==
                    JsonValueKind.Array)
                {
                    foreach (
                        var elem in driversElem.EnumerateArray())
                    {
                        string path =
                            elem.GetString() ?? string.Empty;

                        if ((path.EndsWith(
                                 "slimevr",
                                 StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(
                                 "driver_slimevr",
                                 StringComparison.OrdinalIgnoreCase)) &&
                            Directory.Exists(path))
                        {
                            return path;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to SteamVR's normal driver path.
            }
        }

        string steamPath = DetectSteamPath();

        if (!string.IsNullOrEmpty(steamPath))
        {
            string candidate = Path.Combine(
                steamPath,
                "steamapps",
                "common",
                "SteamVR",
                "drivers",
                "slimevr"
            );

            if (Directory.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }

    public bool IsValidSlimeVRDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Current Electron SlimeVR installation.
            if (File.Exists(Path.Combine(path, "slimevr.exe")))
                return true;

            if (File.Exists(Path.Combine(path, "slimevr.jar")))
                return true;

            if (File.Exists(
                    Path.Combine(
                        path,
                        "resources",
                        "slimevr.jar")))
            {
                return true;
            }

            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (path.EndsWith(
                    ".app",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Directory.Exists(path);
            }
        }

        // Linux/package layouts.
        return File.Exists(Path.Combine(path, "slimevr")) ||
               File.Exists(Path.Combine(path, "slimevr.jar")) ||
               File.Exists(
                   Path.Combine(path, "resources", "slimevr.jar"));
    }

    private static void AddCandidate(
        ICollection<string> candidates,
        params string[] parts)
    {
        if (parts.Length == 0 ||
            parts.Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        string candidate = Path.Combine(parts);

        if (!candidates.Contains(
                candidate,
                StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(candidate);
        }
    }

    private string GetOpenVRPathsFile()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                );

            return Path.Combine(
                localAppData,
                "openvr",
                "openvrpaths.vrpath"
            );
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string home =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            return Path.Combine(
                home,
                ".config",
                "openvr",
                "openvrpaths.vrpath"
            );
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile
                );

            return Path.Combine(
                home,
                "Library",
                "Application Support",
                "OpenVR",
                "openvrpaths.vrpath"
            );
        }

        return string.Empty;
    }
}