using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace SlimeVRExtender.Services;

public class ReplacerEngine
{
    private readonly PlatformService _platformService;

    public ReplacerEngine(PlatformService platformService)
    {
        _platformService = platformService;
    }

    public void StopRunningProcesses()
    {
        string[] targetProcessNames = ["slimevr", "slimevr-gui", "SlimeVR", "slimevr.exe", "slimevr-gui.exe"];
        foreach (var name in targetProcessNames)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(name.Replace(".exe", "")))
                {
                    try
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    public string CreateBackup(string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory)) return string.Empty;

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupDir = $"{targetDirectory}_backup_{timestamp}";

        try
        {
            CopyDirectory(targetDirectory, backupDir);
            return backupDir;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to create backup: {ex.Message}", ex);
        }
    }

    public async Task ReplaceReleaseFilesAsync(string archiveFilePath, string targetDirectory, bool preserveConfig = true)
    {
        StopRunningProcesses();

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }
        else
        {
            CreateBackup(targetDirectory);
        }

        var configBackup = new Dictionary<string, byte[]>();
        if (preserveConfig)
        {
            string[] configFiles = ["slimevr.config.json", "slimevr.settings.json", "config.json"];
            foreach (var cf in configFiles)
            {
                string fullPath = Path.Combine(targetDirectory, cf);
                if (File.Exists(fullPath))
                {
                    configBackup[cf] = await File.ReadAllBytesAsync(fullPath);
                }
            }
        }

        string tempExtract = Path.Combine(Path.GetTempPath(), "SlimeVR_Extract_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempExtract);

            if (archiveFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archiveFilePath, tempExtract);
            }
            else
            {
                ZipFile.ExtractToDirectory(archiveFilePath, tempExtract);
            }

            CopyDirectory(tempExtract, targetDirectory);

            string? driverZipInExtract = Directory.GetFiles(tempExtract, "slimevr-openvr-driver-*.zip", SearchOption.AllDirectories).FirstOrDefault();
            if (driverZipInExtract != null)
            {
                string internalDriverTarget = Path.Combine(targetDirectory, "driver");
                Directory.CreateDirectory(internalDriverTarget);
                ZipFile.ExtractToDirectory(driverZipInExtract, internalDriverTarget, true);
            }

            foreach (var kvp in configBackup)
            {
                string restoredPath = Path.Combine(targetDirectory, kvp.Key);
                await File.WriteAllBytesAsync(restoredPath, kvp.Value);
            }
        }
        finally
        {
            if (Directory.Exists(tempExtract))
            {
                try { Directory.Delete(tempExtract, true); } catch { }
            }
        }
    }

    public async Task ReplaceDriverFilesAsync(string driverZipPath, string driverTargetDirectory)
    {
        StopRunningProcesses();

        if (!Directory.Exists(driverTargetDirectory))
        {
            Directory.CreateDirectory(driverTargetDirectory);
        }
        else
        {
            CreateBackup(driverTargetDirectory);
        }

        string tempExtract = Path.Combine(Path.GetTempPath(), "SlimeVR_Driver_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempExtract);
            ZipFile.ExtractToDirectory(driverZipPath, tempExtract);

            CopyDirectory(tempExtract, driverTargetDirectory);
        }
        finally
        {
            if (Directory.Exists(tempExtract))
            {
                try { Directory.Delete(tempExtract, true); } catch { }
            }
        }
    }

    public async Task SelfUpdateAppAsync(string archiveFilePath)
    {
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(currentExePath))
            throw new Exception("Could not determine current executable path.");

        string currentDir = Path.GetDirectoryName(currentExePath)!;
        string tempExtract = Path.Combine(Path.GetTempPath(), "SlimeVRExtender_SelfUpdate_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempExtract);
        if (archiveFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archiveFilePath, tempExtract);
        }
        else
        {
            ZipFile.ExtractToDirectory(archiveFilePath, tempExtract);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string batchScriptPath = Path.Combine(Path.GetTempPath(), "update_extender.bat");
            string scriptContent = $@"@echo off
timeout /t 2 /nobreak > NUL
xcopy /Y /S /E ""{tempExtract}\*"" ""{currentDir}\""
start """" ""{currentExePath}""
del ""%~f0""
";
            await File.WriteAllTextAsync(batchScriptPath, scriptContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchScriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Environment.Exit(0);
        }
        else
        {
            string shScriptPath = Path.Combine(Path.GetTempPath(), "update_extender.sh");
            string scriptContent = $@"#!/bin/sh
sleep 2
cp -r '{tempExtract}'/* '{currentDir}/'
chmod +x '{currentExePath}'
'{currentExePath}' &
rm -- ""$0""
";
            await File.WriteAllTextAsync(shScriptPath, scriptContent);
            Process.Start("chmod", $"+x \"{shScriptPath}\"");

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"\"{shScriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            Environment.Exit(0);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, file);
            string destFile = Path.Combine(destinationDir, relativePath);

            string? destDir = Path.GetDirectoryName(destFile);
            if (destDir != null && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(file, destFile, true);
        }
    }
}
