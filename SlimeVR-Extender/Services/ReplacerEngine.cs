using System.Diagnostics;
using System.Formats.Tar;
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
        string[] targetProcessNames =
        [
            "slimevr",
            "slimevr-gui",
            "SlimeVR",
            "slimevr.exe",
            "slimevr-gui.exe"
        ];

        foreach (string name in targetProcessNames)
        {
            try
            {
                string processName =
                    name.Replace(
                        ".exe",
                        "",
                        StringComparison.OrdinalIgnoreCase
                    );

                foreach (
                    var proc in
                    Process.GetProcessesByName(processName))
                {
                    try
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
            }
        }
    }

    public string CreateBackup(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException(
                "Target directory cannot be empty.",
                nameof(targetDirectory)
            );
        }

        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                $"SlimeVR target directory does not exist: " +
                $"{targetDirectory}"
            );
        }

        string timestamp =
            DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string backupDir =
            $"{targetDirectory}_backup_{timestamp}";

        try
        {
            CopyDirectory(
                targetDirectory,
                backupDir
            );

            return backupDir;
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Failed to create backup: {ex.Message}",
                ex
            );
        }
    }

    public async Task ReplaceReleaseFilesAsync(
        string archiveFilePath,
        string targetDirectory,
        bool preserveConfig = true)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) ||
            !File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException(
                "The SlimeVR release archive does not exist.",
                archiveFilePath
            );
        }

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                "No SlimeVR installation was detected. " +
                "Select the existing SlimeVR installation directory."
            );
        }

        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The selected SlimeVR installation does not exist: " +
                $"{targetDirectory}"
            );
        }

        StopRunningProcesses();

        CreateBackup(targetDirectory);

        var configBackup =
            new Dictionary<string, byte[]>(
                StringComparer.OrdinalIgnoreCase
            );

        if (preserveConfig)
        {
            string[] configFiles =
            [
                "slimevr.config.json",
                "slimevr.settings.json",
                "config.json"
            ];

            foreach (string configFile in configFiles)
            {
                string fullPath =
                    Path.Combine(
                        targetDirectory,
                        configFile
                    );

                if (File.Exists(fullPath))
                {
                    configBackup[configFile] =
                        await File.ReadAllBytesAsync(fullPath);
                }
            }
        }

        string tempExtract = Path.Combine(
            Path.GetTempPath(),
            "SlimeVR_Extract_" +
            Guid.NewGuid().ToString("N")
        );

        try
        {
            Directory.CreateDirectory(tempExtract);

            await ExtractArchiveAsync(
                archiveFilePath,
                tempExtract
            );

            string copyRoot =
                GetArchiveContentRoot(tempExtract);

            CopyDirectory(
                copyRoot,
                targetDirectory
            );

            string? driverZipInExtract =
                Directory
                    .GetFiles(
                        copyRoot,
                        "slimevr-openvr-driver-*.zip",
                        SearchOption.AllDirectories
                    )
                    .FirstOrDefault();

            if (driverZipInExtract != null)
            {
                string internalDriverTarget =
                    Path.Combine(
                        targetDirectory,
                        "driver"
                    );

                Directory.CreateDirectory(
                    internalDriverTarget
                );

                ZipFile.ExtractToDirectory(
                    driverZipInExtract,
                    internalDriverTarget,
                    true
                );
            }

            foreach (var kvp in configBackup)
            {
                string restoredPath =
                    Path.Combine(
                        targetDirectory,
                        kvp.Key
                    );

                await File.WriteAllBytesAsync(
                    restoredPath,
                    kvp.Value
                );
            }
        }
        finally
        {
            if (Directory.Exists(tempExtract))
            {
                try
                {
                    Directory.Delete(
                        tempExtract,
                        true
                    );
                }
                catch
                {
                }
            }
        }
    }

    public async Task ReplaceDriverFilesAsync(
        string driverZipPath,
        string driverTargetDirectory)
    {
        if (string.IsNullOrWhiteSpace(driverZipPath) ||
            !File.Exists(driverZipPath))
        {
            throw new FileNotFoundException(
                "The SlimeVR driver archive does not exist.",
                driverZipPath
            );
        }

        if (string.IsNullOrWhiteSpace(driverTargetDirectory))
        {
            throw new DirectoryNotFoundException(
                "No SteamVR SlimeVR driver path was detected."
            );
        }

        StopRunningProcesses();

        if (!Directory.Exists(driverTargetDirectory))
        {
            Directory.CreateDirectory(
                driverTargetDirectory
            );
        }
        else
        {
            CreateBackup(
                driverTargetDirectory
            );
        }

        string tempExtract = Path.Combine(
            Path.GetTempPath(),
            "SlimeVR_Driver_" +
            Guid.NewGuid().ToString("N")
        );

        try
        {
            Directory.CreateDirectory(tempExtract);

            ZipFile.ExtractToDirectory(
                driverZipPath,
                tempExtract
            );

            string copyRoot =
                GetArchiveContentRoot(tempExtract);

            CopyDirectory(
                copyRoot,
                driverTargetDirectory
            );
        }
        finally
        {
            if (Directory.Exists(tempExtract))
            {
                try
                {
                    Directory.Delete(
                        tempExtract,
                        true
                    );
                }
                catch
                {
                }
            }
        }

        await Task.CompletedTask;
    }

    public async Task SelfUpdateAppAsync(
        string archiveFilePath)
    {
        string currentExePath =
            Process.GetCurrentProcess()
                .MainModule?.FileName ??
            Environment.ProcessPath ??
            string.Empty;

        if (string.IsNullOrEmpty(currentExePath))
        {
            throw new Exception(
                "Could not determine current executable path."
            );
        }

        string currentDir =
            Path.GetDirectoryName(currentExePath)
            ?? throw new Exception(
                "Could not determine current application directory."
            );

        string tempExtract = Path.Combine(
            Path.GetTempPath(),
            "SlimeVRExtender_SelfUpdate_" +
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(tempExtract);

        await ExtractArchiveAsync(
            archiveFilePath,
            tempExtract
        );

        string copyRoot =
            GetArchiveContentRoot(tempExtract);

        if (RuntimeInformation.IsOSPlatform(
                OSPlatform.Windows))
        {
            string batchScriptPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "update_extender.bat"
                );

            string scriptContent =
                $@"@echo off
timeout /t 2 /nobreak > NUL
xcopy /Y /S /E ""{copyRoot}\*"" ""{currentDir}\""
start """" ""{currentExePath}""
rmdir /S /Q ""{tempExtract}""
del ""%~f0""
";

            await File.WriteAllTextAsync(
                batchScriptPath,
                scriptContent
            );

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments =
                        $"/c \"{batchScriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            );

            Environment.Exit(0);
        }
        else
        {
            string shScriptPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "update_extender.sh"
                );

            string escapedCopyRoot =
                EscapeShellSingleQuoted(copyRoot);

            string escapedCurrentDir =
                EscapeShellSingleQuoted(currentDir);

            string escapedCurrentExe =
                EscapeShellSingleQuoted(currentExePath);

            string escapedTempExtract =
                EscapeShellSingleQuoted(tempExtract);

            string scriptContent =
                $@"#!/bin/sh
sleep 2
cp -r '{escapedCopyRoot}'/. '{escapedCurrentDir}/'
chmod +x '{escapedCurrentExe}'
'{escapedCurrentExe}' &
rm -rf '{escapedTempExtract}'
rm -- ""$0""
";

            await File.WriteAllTextAsync(
                shScriptPath,
                scriptContent
            );

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments =
                        $"+x \"{shScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            )?.WaitForExit();

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments =
                        $"\"{shScriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            );

            Environment.Exit(0);
        }
    }

    private static async Task ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory)
    {
        if (archiveFilePath.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(
                archiveFilePath,
                destinationDirectory
            );

            return;
        }

        if (archiveFilePath.EndsWith(
                ".tar.gz",
                StringComparison.OrdinalIgnoreCase) ||
            archiveFilePath.EndsWith(
                ".tgz",
                StringComparison.OrdinalIgnoreCase))
        {
            await using var fileStream =
                File.OpenRead(archiveFilePath);

            await using var gzipStream =
                new GZipStream(
                    fileStream,
                    CompressionMode.Decompress
                );

            TarFile.ExtractToDirectory(
                gzipStream,
                destinationDirectory,
                overwriteFiles: true
            );

            return;
        }

        throw new NotSupportedException(
            $"Unsupported release archive format: " +
            $"{Path.GetFileName(archiveFilePath)}"
        );
    }

    private static string GetArchiveContentRoot(
        string extractedDirectory)
    {
        string[] files =
            Directory.GetFiles(
                extractedDirectory,
                "*",
                SearchOption.TopDirectoryOnly
            );

        string[] directories =
            Directory.GetDirectories(
                extractedDirectory,
                "*",
                SearchOption.TopDirectoryOnly
            );

        if (files.Length == 0 &&
            directories.Length == 1)
        {
            return directories[0];
        }

        return extractedDirectory;
    }

    private static string EscapeShellSingleQuoted(
        string value)
    {
        return value.Replace(
            "'",
            "'\"'\"'"
        );
    }

    private static void CopyDirectory(
        string sourceDir,
        string destinationDir)
    {
        Directory.CreateDirectory(
            destinationDir
        );

        foreach (
            string file in Directory.GetFiles(
                sourceDir,
                "*",
                SearchOption.AllDirectories))
        {
            string relativePath =
                Path.GetRelativePath(
                    sourceDir,
                    file
                );

            string destFile =
                Path.Combine(
                    destinationDir,
                    relativePath
                );

            string? destDir =
                Path.GetDirectoryName(destFile);

            if (destDir != null &&
                !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(
                file,
                destFile,
                true
            );
        }
    }
}