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
                    return Path.GetFullPath(steamPath);
                }
            }
            catch
            {
            }

            string programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86
                );

            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                string path =
                    Path.Combine(programFilesX86, "Steam");

                if (Directory.Exists(path))
                    return path;
            }

            string programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                );

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                string path =
                    Path.Combine(programFiles, "Steam");

                if (Directory.Exists(path))
                    return path;
            }
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

            string path =
                Path.Combine(
                    home,
                    "Library",
                    "Application Support",
                    "Steam"
                );

            if (Directory.Exists(path))
                return path;
        }

        return string.Empty;
    }

    public string DetectSteamSlimeVRPath()
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        var candidates = new List<string>();

        string detectedSteam = DetectSteamPath();

        if (!string.IsNullOrWhiteSpace(detectedSteam))
        {
            AddSteamSlimeVRCandidates(
                candidates,
                detectedSteam
            );
        }

        string programFilesX86 =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86
            );

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            AddSteamSlimeVRCandidates(
                candidates,
                Path.Combine(programFilesX86, "Steam")
            );
        }

        string programFiles =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles
            );

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            AddSteamSlimeVRCandidates(
                candidates,
                Path.Combine(programFiles, "Steam")
            );
        }

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady ||
                    drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                string[] possibleSteamRoots =
                [
                    Path.Combine(
                        drive.RootDirectory.FullName,
                        "SteamLibrary"
                    ),
                    Path.Combine(
                        drive.RootDirectory.FullName,
                        "Steam"
                    )
                ];

                foreach (string steamRoot in possibleSteamRoots)
                {
                    if (!Directory.Exists(steamRoot))
                        continue;

                    AddSteamSlimeVRCandidates(
                        candidates,
                        steamRoot
                    );
                }
            }
            catch
            {
            }
        }

        foreach (string candidate in candidates.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            if (IsValidSteamSlimeVRDirectory(candidate))
                return Path.GetFullPath(candidate);
        }

        return string.Empty;
    }

    private static void AddSteamSlimeVRCandidates(
        ICollection<string> candidates,
        string steamRoot)
    {
        if (string.IsNullOrWhiteSpace(steamRoot))
            return;

        candidates.Add(
            Path.Combine(
                steamRoot,
                "steamapps",
                "common",
                "SlimeVR"
            )
        );

        candidates.Add(
            Path.Combine(
                steamRoot,
                "steamapps",
                "common",
                "SlimeVR Server"
            )
        );
    }

    private static bool IsValidSteamSlimeVRDirectory(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path))
        {
            return false;
        }

        string applicationDirectory =
            Path.Combine(
                path,
                "x64",
                "SlimeVR"
            );

        return
            Directory.Exists(applicationDirectory) &&
            (
                File.Exists(
                    Path.Combine(
                        applicationDirectory,
                        "SlimeVR.exe"
                    )
                ) ||
                File.Exists(
                    Path.Combine(
                        applicationDirectory,
                        "slimevr.exe"
                    )
                )
            ) &&
            File.Exists(
                Path.Combine(
                    applicationDirectory,
                    "slimevr.jar"
                )
            );
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
                if (IsValidStandaloneSlimeVRDirectory(candidate))
                    return Path.GetFullPath(candidate);
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
            const string candidate =
                "/Applications/SlimeVR.app";

            return Directory.Exists(candidate)
                ? candidate
                : string.Empty;
        }

        return string.Empty;
    }

    private static bool IsValidStandaloneSlimeVRDirectory(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path))
        {
            return false;
        }

        return
            File.Exists(
                Path.Combine(path, "slimevr.exe")
            ) ||
            File.Exists(
                Path.Combine(path, "SlimeVR.exe")
            ) ||
            File.Exists(
                Path.Combine(path, "slimevr.jar")
            ) ||
            File.Exists(
                Path.Combine(
                    path,
                    "resources",
                    "slimevr.jar"
                )
            );
    }

    public string DetectSteamVRDriverPath()
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        string steamPath = DetectSteamPath();

        if (string.IsNullOrWhiteSpace(steamPath))
            return string.Empty;

        string steamVrPath =
            Path.Combine(
                steamPath,
                "steamapps",
                "common",
                "SteamVR"
            );

        if (!Directory.Exists(steamVrPath))
            return string.Empty;

        return Path.Combine(
            steamVrPath,
            "drivers",
            "slimevr"
        );
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
            if (File.Exists(
                    Path.Combine(path, "slimevr.exe")))
            {
                return true;
            }

            if (File.Exists(
                    Path.Combine(path, "SlimeVR.exe")))
            {
                return true;
            }

            if (File.Exists(
                    Path.Combine(path, "slimevr.jar")))
            {
                return true;
            }

            if (File.Exists(
                    Path.Combine(
                        path,
                        "resources",
                        "slimevr.jar")))
            {
                return true;
            }

            string steamApplication =
                Path.Combine(
                    path,
                    "x64",
                    "SlimeVR"
                );

            return
                File.Exists(
                    Path.Combine(
                        steamApplication,
                        "SlimeVR.exe"
                    )
                ) ||
                File.Exists(
                    Path.Combine(
                        steamApplication,
                        "slimevr.exe"
                    )
                ) ||
                File.Exists(
                    Path.Combine(
                        steamApplication,
                        "slimevr.jar"
                    )
                );
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

        return
            File.Exists(
                Path.Combine(path, "slimevr")
            ) ||
            File.Exists(
                Path.Combine(path, "slimevr.jar")
            ) ||
            File.Exists(
                Path.Combine(
                    path,
                    "resources",
                    "slimevr.jar"
                )
            );
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

        string candidate =
            Path.Combine(parts);

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