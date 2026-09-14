using System.Runtime.InteropServices;

namespace SlimeVRExtender.Services;

public enum TargetPlatform
{
    WindowsX64,
    LinuxX64,
    LinuxArm64,
    MacOsX64,
    MacOsArm64,
    Unknown
}

public class PlatformService
{
    public TargetPlatform CurrentPlatform { get; }

    public PlatformService()
    {
        CurrentPlatform = DetectPlatform();
    }

    private static TargetPlatform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TargetPlatform.WindowsX64;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => TargetPlatform.LinuxArm64,
                _ => TargetPlatform.LinuxX64
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => TargetPlatform.MacOsArm64,
                _ => TargetPlatform.MacOsX64
            };
        }

        return TargetPlatform.Unknown;
    }

    public string GetDriverZipName()
    {
        return CurrentPlatform switch
        {
            TargetPlatform.WindowsX64 => "slimevr-openvr-driver-win64.zip",
            TargetPlatform.LinuxX64 => "slimevr-openvr-driver-x64-linux.zip",
            TargetPlatform.LinuxArm64 => "slimevr-openvr-driver-aarch64-linux.zip",
            _ => string.Empty
        };
    }

    public string GetReleaseAssetKeyword()
    {
        return CurrentPlatform switch
        {
            TargetPlatform.WindowsX64 => "windows",
            TargetPlatform.LinuxX64 => "linux-x64",
            TargetPlatform.LinuxArm64 => "linux-aarch64",
            TargetPlatform.MacOsX64 or TargetPlatform.MacOsArm64 => "macos",
            _ => "windows"
        };
    }

    public string GetPlatformDisplayName()
    {
        return CurrentPlatform switch
        {
            TargetPlatform.WindowsX64 => "Windows (x64)",
            TargetPlatform.LinuxX64 => "Linux (x64)",
            TargetPlatform.LinuxArm64 => "Linux (ARM64)",
            TargetPlatform.MacOsX64 => "macOS (Intel)",
            TargetPlatform.MacOsArm64 => "macOS (Apple Silicon)",
            _ => "Unknown Platform"
        };
    }
}
