using System.Diagnostics;
using System.IO.Compression;

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

        // Save existing config files to memory before extraction
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

        // Extract package
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
                // Fallback for zip/tar extraction
                ZipFile.ExtractToDirectory(archiveFilePath, tempExtract);
            }

            // Copy extracted files to target directory
            CopyDirectory(tempExtract, targetDirectory);

            // If a nested driver zip was contained within the release, unpack it to driver subfolder if applicable
            string? driverZipInExtract = Directory.GetFiles(tempExtract, "slimevr-openvr-driver-*.zip", SearchOption.AllDirectories).FirstOrDefault();
            if (driverZipInExtract != null)
            {
                string internalDriverTarget = Path.Combine(targetDirectory, "driver");
                Directory.CreateDirectory(internalDriverTarget);
                ZipFile.ExtractToDirectory(driverZipInExtract, internalDriverTarget, true);
            }

            // Restore user configuration files
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

            // Copy extracted driver files (bin, resources, driver.vrdrivermanifest) into target folder
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
