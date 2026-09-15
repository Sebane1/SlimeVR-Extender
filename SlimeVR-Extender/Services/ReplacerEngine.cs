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
            "SlimeVR"
        ];

        foreach (string name in targetProcessNames)
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill(true);
                        process.WaitForExit(3000);
                    }
                    catch
                    {
                        // Ignore processes that cannot be terminated.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore process enumeration errors.
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
                $"Target directory does not exist: {targetDirectory}"
            );
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string backupDirectory =
            $"{targetDirectory}_backup_{timestamp}";

        int copiedFiles = CopyDirectoryWithCount(
            targetDirectory,
            backupDirectory
        );

        if (copiedFiles == 0)
        {
            throw new IOException(
                $"Backup of '{targetDirectory}' contained zero files."
            );
        }

        return backupDirectory;
    }

    public async Task ReplaceReleaseFilesAsync(
        string archiveFilePath,
        string targetDirectory,
        bool preserveConfig = true,
        IProgress<string>? progress = null,
        string? driverTargetDirectory = null,
        bool installEmbeddedDriver = false)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath))
        {
            throw new ArgumentException(
                "Release archive path cannot be empty.",
                nameof(archiveFilePath)
            );
        }

        archiveFilePath = Path.GetFullPath(archiveFilePath);

        if (!File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException(
                "Release archive does not exist.",
                archiveFilePath
            );
        }

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                "No SlimeVR installation directory was provided."
            );
        }

        targetDirectory = Path.GetFullPath(targetDirectory);

        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                $"SlimeVR installation does not exist: {targetDirectory}"
            );
        }

        progress?.Report("Stopping SlimeVR...");

        StopRunningProcesses();

        progress?.Report("Creating SlimeVR backup...");

        string backupDirectory =
            CreateBackup(targetDirectory);

        progress?.Report(
            $"Backup created:\n{backupDirectory}"
        );


        Dictionary<string, byte[]> configBackup =
            new(StringComparer.OrdinalIgnoreCase);

        if (preserveConfig)
        {
            progress?.Report(
                "Preserving SlimeVR configuration..."
            );

            string[] configFiles =
            [
                "slimevr.config.json",
                "slimevr.settings.json",
                "config.json"
            ];

            foreach (string configFile in configFiles)
            {
                string path =
                    Path.Combine(
                        targetDirectory,
                        configFile
                    );

                if (File.Exists(path))
                {
                    configBackup[configFile] =
                        await File.ReadAllBytesAsync(path);
                }
            }
        }
        string tempExtract = Path.Combine(
            Path.GetTempPath(),
            "SlimeVR_Extract_" +
            Guid.NewGuid().ToString("N")
        );

        bool successful = false;

        try
        {
            Directory.CreateDirectory(tempExtract);

            progress?.Report(
                $"Extracting release bundle:\n" +
                $"{Path.GetFileName(archiveFilePath)}"
            );

            await ExtractArchiveAsync(
                archiveFilePath,
                tempExtract
            );

            string[] outerFiles =
                Directory.GetFiles(
                    tempExtract,
                    "*",
                    SearchOption.AllDirectories
                );

            if (outerFiles.Length == 0)
            {
                throw new InvalidDataException(
                    "The release bundle extracted zero files."
                );
            }

            progress?.Report(
                $"Release bundle contains {outerFiles.Length} payload files."
            );

            if (OperatingSystem.IsWindows())
            {
                await InstallWindowsBundleAsync(
                    outerFiles,
                    tempExtract,
                    targetDirectory,
                    driverTargetDirectory,
                    installEmbeddedDriver,
                    progress
                );
            }
            else
            {
                // Linux/macOS packages may already contain the actual
                // application files rather than the nested Windows ZIP.
                string copyRoot =
                    GetArchiveContentRoot(tempExtract);

                int copiedFiles =
                    CopyDirectoryWithCount(
                        copyRoot,
                        targetDirectory
                    );

                if (copiedFiles == 0)
                {
                    throw new IOException(
                        "No SlimeVR files were installed."
                    );
                }

                progress?.Report(
                    $"Installed {copiedFiles} SlimeVR files."
                );
            }

            if (configBackup.Count > 0)
            {
                progress?.Report(
                    "Restoring SlimeVR configuration..."
                );

                foreach (var entry in configBackup)
                {
                    string destination =
                        Path.Combine(
                            targetDirectory,
                            entry.Key
                        );

                    await File.WriteAllBytesAsync(
                        destination,
                        entry.Value
                    );
                }
            }

            if (OperatingSystem.IsWindows())
            {
                string installedExe =
                    Path.Combine(
                        targetDirectory,
                        "slimevr.exe"
                    );

                if (!File.Exists(installedExe))
                {
                    throw new IOException(
                        "Update completed, but slimevr.exe was not found " +
                        $"in the installation directory:\n{installedExe}"
                    );
                }
            }

            successful = true;

            progress?.Report(
                "SlimeVR replacement completed successfully."
            );
        }
        catch (Exception ex)
        {
            progress?.Report(
                $"Update failed. Temporary extraction has been preserved:\n" +
                $"{tempExtract}"
            );

            throw new Exception(
                "SlimeVR replacement failed.\n\n" +
                $"Archive: {archiveFilePath}\n" +
                $"Target: {targetDirectory}\n" +
                $"Backup: {backupDirectory}\n" +
                $"Temporary extraction: {tempExtract}\n\n" +
                $"{ex.GetType().Name}: {ex.Message}",
                ex
            );
        }
        finally
        {
            // Preserve failed extraction directories for debugging.
            if (successful &&
                Directory.Exists(tempExtract))
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
                    // Cleanup failure does not invalidate the update.
                }
            }
        }
    }

    private async Task InstallWindowsBundleAsync(
        string[] outerFiles,
        string tempExtract,
        string targetDirectory,
        string? driverTargetDirectory,
        bool installEmbeddedDriver,
        IProgress<string>? progress)
    {

        string? applicationZip =
            outerFiles.FirstOrDefault(file =>
            {
                string name =
                    Path.GetFileName(file);

                return
                    name.StartsWith(
                        "SlimeVR-win-",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    name.EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase
                    );
            });

        // Also support official naming such as SlimeVR-win64.zip.
        applicationZip ??=
            outerFiles.FirstOrDefault(file =>
            {
                string name =
                    Path.GetFileName(file);

                return
                    name.StartsWith(
                        "SlimeVR-win",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    name.EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !name.StartsWith(
                        "slimevr-openvr-driver",
                        StringComparison.OrdinalIgnoreCase
                    );
            });

        if (applicationZip == null)
        {
            throw new InvalidDataException(
                "The Windows release bundle does not contain " +
                "a SlimeVR-win*.zip application package."
            );
        }

        progress?.Report(
            $"Found SlimeVR application package:\n" +
            $"{Path.GetFileName(applicationZip)}"
        );

        string applicationExtractDirectory =
            Path.Combine(
                tempExtract,
                "SlimeVR_Application"
            );

        Directory.CreateDirectory(
            applicationExtractDirectory
        );

        progress?.Report(
            "Extracting SlimeVR application..."
        );

        ZipFile.ExtractToDirectory(
            applicationZip,
            applicationExtractDirectory,
            overwriteFiles: true
        );

        string applicationRoot =
            GetArchiveContentRoot(
                applicationExtractDirectory
            );

        string? packagedExe =
            Directory
                .GetFiles(
                    applicationRoot,
                    "slimevr.exe",
                    SearchOption.AllDirectories
                )
                .FirstOrDefault();

        if (packagedExe == null)
        {
            throw new InvalidDataException(
                $"The application archive '{Path.GetFileName(applicationZip)}' " +
                "does not contain slimevr.exe."
            );
        }

        applicationRoot =
            Path.GetDirectoryName(packagedExe)
            ?? applicationRoot;

        string[] applicationFiles =
            Directory.GetFiles(
                applicationRoot,
                "*",
                SearchOption.AllDirectories
            );

        if (applicationFiles.Length == 0)
        {
            throw new InvalidDataException(
                "The SlimeVR application package extracted zero files."
            );
        }

        progress?.Report(
            $"Application package contains " +
            $"{applicationFiles.Length} files."
        );

        int copiedApplicationFiles =
            CopyDirectoryWithCount(
                applicationRoot,
                targetDirectory
            );

        if (copiedApplicationFiles == 0)
        {
            throw new IOException(
                "Zero SlimeVR application files were installed."
            );
        }

        progress?.Report(
            $"Installed {copiedApplicationFiles} application files."
        );

        string installedExe =
            Path.Combine(
                targetDirectory,
                "slimevr.exe"
            );

        if (!File.Exists(installedExe))
        {
            throw new IOException(
                $"slimevr.exe was not installed:\n{installedExe}"
            );
        }

        long packagedExeSize =
            new FileInfo(packagedExe).Length;

        long installedExeSize =
            new FileInfo(installedExe).Length;

        if (packagedExeSize != installedExeSize)
        {
            throw new IOException(
                "slimevr.exe verification failed.\n\n" +
                $"Package: {packagedExeSize:N0} bytes\n" +
                $"Installed: {installedExeSize:N0} bytes"
            );
        }

        progress?.Report(
            "slimevr.exe replacement verified."
        );

        string? serverJar =
            outerFiles.FirstOrDefault(file =>
                string.Equals(
                    Path.GetFileName(file),
                    "slimevr.jar",
                    StringComparison.OrdinalIgnoreCase
                )
            );

        if (serverJar == null)
        {
            throw new InvalidDataException(
                "The release bundle does not contain slimevr.jar."
            );
        }

        string jarDestination =
            FindJarDestination(
                targetDirectory
            );

        progress?.Report(
            $"Installing slimevr.jar to:\n{jarDestination}"
        );

        File.Copy(
            serverJar,
            jarDestination,
            overwrite: true
        );

        if (!File.Exists(jarDestination))
        {
            throw new IOException(
                $"slimevr.jar was not installed:\n{jarDestination}"
            );
        }

        if (new FileInfo(serverJar).Length !=
            new FileInfo(jarDestination).Length)
        {
            throw new IOException(
                "slimevr.jar replacement verification failed."
            );
        }

        progress?.Report(
            "slimevr.jar replacement verified."
        );

        string? embeddedDriverZip =
            outerFiles.FirstOrDefault(file =>
            {
                string name =
                    Path.GetFileName(file);

                return
                    name.StartsWith(
                        "slimevr-openvr-driver-",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    name.EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase
                    );
            });

        if (installEmbeddedDriver)
        {
            if (embeddedDriverZip == null)
            {
                throw new InvalidDataException(
                    "OpenVR driver installation was requested, " +
                    "but the release bundle does not contain a driver ZIP."
                );
            }

            if (string.IsNullOrWhiteSpace(
                    driverTargetDirectory))
            {
                throw new DirectoryNotFoundException(
                    "OpenVR driver installation was requested, " +
                    "but no SteamVR SlimeVR driver path was provided."
                );
            }

            progress?.Report(
                $"Installing embedded OpenVR driver:\n" +
                $"{Path.GetFileName(embeddedDriverZip)}"
            );

            await ReplaceDriverFilesAsync(
                embeddedDriverZip,
                driverTargetDirectory
            );

            progress?.Report(
                "OpenVR driver replacement completed."
            );
        }
    }

    private static string FindJarDestination(
        string targetDirectory)
    {
        // First prefer an existing slimevr.jar. This lets us replace
        // whatever location the installed SlimeVR version already uses.

        string[] existingJars =
            Directory.GetFiles(
                targetDirectory,
                "slimevr.jar",
                SearchOption.AllDirectories
            );

        if (existingJars.Length > 0)
        {
            // Prefer root-level slimevr.jar if present.
            string rootJar =
                Path.Combine(
                    targetDirectory,
                    "slimevr.jar"
                );

            string? exactRootJar =
                existingJars.FirstOrDefault(path =>
                    string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(rootJar),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (exactRootJar != null)
            {
                return exactRootJar;
            }

            // Otherwise use the first existing JAR.
            return existingJars[0];
        }

        // No existing JAR. Use installation root.
        return Path.Combine(
            targetDirectory,
            "slimevr.jar"
        );
    }

    public async Task ReplaceDriverFilesAsync(
        string driverZipPath,
        string driverTargetDirectory)
    {
        if (string.IsNullOrWhiteSpace(driverZipPath) ||
            !File.Exists(driverZipPath))
        {
            throw new FileNotFoundException(
                "OpenVR driver archive does not exist.",
                driverZipPath
            );
        }

        if (string.IsNullOrWhiteSpace(
                driverTargetDirectory))
        {
            throw new DirectoryNotFoundException(
                "No SteamVR SlimeVR driver path was provided."
            );
        }

        driverTargetDirectory =
            Path.GetFullPath(
                driverTargetDirectory
            );

        StopRunningProcesses();

        if (Directory.Exists(driverTargetDirectory))
        {
            string[] existingFiles =
                Directory.GetFiles(
                    driverTargetDirectory,
                    "*",
                    SearchOption.AllDirectories
                );

            if (existingFiles.Length > 0)
            {
                CreateBackup(
                    driverTargetDirectory
                );
            }
        }
        else
        {
            Directory.CreateDirectory(
                driverTargetDirectory
            );
        }

        string tempExtract =
            Path.Combine(
                Path.GetTempPath(),
                "SlimeVR_Driver_" +
                Guid.NewGuid().ToString("N")
            );

        bool successful = false;

        try
        {
            Directory.CreateDirectory(
                tempExtract
            );

            ZipFile.ExtractToDirectory(
                driverZipPath,
                tempExtract,
                overwriteFiles: true
            );

            string driverRoot =
                GetArchiveContentRoot(
                    tempExtract
                );

            string[] driverFiles =
                Directory.GetFiles(
                    driverRoot,
                    "*",
                    SearchOption.AllDirectories
                );

            if (driverFiles.Length == 0)
            {
                throw new InvalidDataException(
                    "The OpenVR driver ZIP extracted zero files."
                );
            }

            string? driverManifest =
                Directory
                    .GetFiles(
                        driverRoot,
                        "driver.vrdrivermanifest",
                        SearchOption.AllDirectories
                    )
                    .FirstOrDefault();

            if (driverManifest != null)
            {
                driverRoot =
                    Path.GetDirectoryName(
                        driverManifest
                    ) ?? driverRoot;
            }

            int copiedFiles =
                CopyDirectoryWithCount(
                    driverRoot,
                    driverTargetDirectory
                );

            if (copiedFiles == 0)
            {
                throw new IOException(
                    "Zero OpenVR driver files were installed."
                );
            }

            successful = true;
        }
        finally
        {
            if (successful &&
                Directory.Exists(tempExtract))
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

        string currentDirectory =
            Path.GetDirectoryName(currentExePath)
            ?? throw new Exception(
                "Could not determine current application directory."
            );

        string tempExtract =
            Path.Combine(
                Path.GetTempPath(),
                "SlimeVRExtender_SelfUpdate_" +
                Guid.NewGuid().ToString("N")
            );

        Directory.CreateDirectory(
            tempExtract
        );

        await ExtractArchiveAsync(
            archiveFilePath,
            tempExtract
        );

        string copyRoot =
            GetArchiveContentRoot(
                tempExtract
            );

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
xcopy /Y /S /E ""{copyRoot}\*"" ""{currentDirectory}\""
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
            string scriptPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "update_extender.sh"
                );

            string escapedCopyRoot =
                EscapeShellSingleQuoted(
                    copyRoot
                );

            string escapedCurrentDirectory =
                EscapeShellSingleQuoted(
                    currentDirectory
                );

            string escapedCurrentExe =
                EscapeShellSingleQuoted(
                    currentExePath
                );

            string escapedTempExtract =
                EscapeShellSingleQuoted(
                    tempExtract
                );

            string scriptContent =
$@"#!/bin/sh
sleep 2
cp -r '{escapedCopyRoot}'/. '{escapedCurrentDirectory}/'
chmod +x '{escapedCurrentExe}'
'{escapedCurrentExe}' &
rm -rf '{escapedTempExtract}'
rm -- ""$0""
";

            await File.WriteAllTextAsync(
                scriptPath,
                scriptContent
            );

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments =
                        $"+x \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            )?.WaitForExit();

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments =
                        $"\"{scriptPath}\"",
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
        if (!File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException(
                "Archive does not exist.",
                archiveFilePath
            );
        }

        FileInfo archiveInfo =
            new(archiveFilePath);

        if (archiveInfo.Length == 0)
        {
            throw new InvalidDataException(
                $"Archive is empty: {archiveFilePath}"
            );
        }

        if (archiveFilePath.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            // Validate ZIP before extraction.

            using (ZipArchive archive =
                   ZipFile.OpenRead(archiveFilePath))
            {
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException(
                        $"ZIP contains no entries: {archiveFilePath}"
                    );
                }
            }

            ZipFile.ExtractToDirectory(
                archiveFilePath,
                destinationDirectory,
                overwriteFiles: true
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
            await using FileStream fileStream =
                File.OpenRead(
                    archiveFilePath
                );

            await using GZipStream gzipStream =
                new(
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
            $"Unsupported archive format: " +
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

    private static int CopyDirectoryWithCount(
        string sourceDirectory,
        string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Source directory does not exist: {sourceDirectory}"
            );
        }

        Directory.CreateDirectory(
            destinationDirectory
        );

        string[] sourceFiles =
            Directory.GetFiles(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories
            );

        int copiedFiles = 0;

        foreach (string sourceFile in sourceFiles)
        {
            string relativePath =
                Path.GetRelativePath(
                    sourceDirectory,
                    sourceFile
                );

            string destinationFile =
                Path.Combine(
                    destinationDirectory,
                    relativePath
                );

            string? destinationParent =
                Path.GetDirectoryName(
                    destinationFile
                );

            if (!string.IsNullOrEmpty(
                    destinationParent))
            {
                Directory.CreateDirectory(
                    destinationParent
                );
            }

            File.Copy(
                sourceFile,
                destinationFile,
                overwrite: true
            );

            copiedFiles++;
        }

        return copiedFiles;
    }

    private static string EscapeShellSingleQuoted(
        string value)
    {
        return value.Replace(
            "'",
            "'\"'\"'"
        );
    }
}