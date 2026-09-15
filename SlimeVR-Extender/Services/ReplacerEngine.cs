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
                        // Ignore processes we cannot terminate.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore process enumeration failures.
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

        targetDirectory = Path.GetFullPath(targetDirectory);

        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Target directory does not exist: {targetDirectory}"
            );
        }

        string timestamp =
            DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string backupDirectory =
            $"{targetDirectory}_backup_{timestamp}";

        // Avoid a collision if two backups somehow happen
        // within the same second.
        if (Directory.Exists(backupDirectory))
        {
            backupDirectory =
                $"{targetDirectory}_backup_{timestamp}_{Guid.NewGuid():N}";
        }

        int copiedFiles =
            CopyDirectoryWithCount(
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
        ValidateArchive(archiveFilePath);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                "No standalone SlimeVR installation directory was provided."
            );
        }

        archiveFilePath =
            Path.GetFullPath(archiveFilePath);

        targetDirectory =
            Path.GetFullPath(targetDirectory);

        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Standalone SlimeVR installation does not exist: {targetDirectory}"
            );
        }

        progress?.Report("Stopping SlimeVR...");

        StopRunningProcesses();

        progress?.Report(
            "Creating standalone SlimeVR backup..."
        );

        string backupDirectory =
            CreateBackup(targetDirectory);

        progress?.Report(
            $"Backup created: {backupDirectory}"
        );

        Dictionary<string, byte[]> configBackup =
            new(StringComparer.OrdinalIgnoreCase);

        if (preserveConfig)
        {
            progress?.Report(
                "Preserving SlimeVR configuration..."
            );

            PreserveConfiguration(
                targetDirectory,
                configBackup
            );
        }

        string tempExtract =
            CreateTemporaryDirectory(
                "SlimeVR_Extract_"
            );

        bool successful = false;

        try
        {
            progress?.Report(
                $"Extracting release bundle: {Path.GetFileName(archiveFilePath)}"
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
                await InstallStandaloneWindowsBundleAsync(
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
                string copyRoot =
                    GetArchiveContentRoot(
                        tempExtract
                    );

                int copiedFiles =
                    CopyDirectoryWithCount(
                        copyRoot,
                        targetDirectory
                    );

                if (copiedFiles == 0)
                {
                    throw new IOException(
                        "No standalone SlimeVR files were installed."
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

                await RestoreConfigurationAsync(
                    targetDirectory,
                    configBackup
                );
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
                        $"Standalone update completed but slimevr.exe was not found: {installedExe}"
                    );
                }
            }

            successful = true;

            progress?.Report(
                "Standalone SlimeVR replacement completed successfully."
            );
        }
        catch (Exception ex)
        {
            progress?.Report(
                $"Update failed. Temporary extraction preserved at: {tempExtract}"
            );

            throw new Exception(
                "Standalone SlimeVR replacement failed.\n\n" +
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
            if (successful)
            {
                TryDeleteDirectory(
                    tempExtract
                );
            }
        }
    }

    private async Task InstallStandaloneWindowsBundleAsync(
        string[] outerFiles,
        string tempExtract,
        string targetDirectory,
        string? driverTargetDirectory,
        bool installEmbeddedDriver,
        IProgress<string>? progress)
    {
        string applicationZip =
            FindWindowsApplicationZip(
                outerFiles
            );

        string serverJar =
            FindServerJar(
                outerFiles
            );

        string? embeddedDriverZip =
            FindDriverZip(
                outerFiles
            );

        progress?.Report(
            $"Found application package: {Path.GetFileName(applicationZip)}"
        );

        string applicationExtract =
            Path.Combine(
                tempExtract,
                "Standalone_Application"
            );

        Directory.CreateDirectory(
            applicationExtract
        );

        progress?.Report(
            "Extracting standalone application..."
        );

        ZipFile.ExtractToDirectory(
            applicationZip,
            applicationExtract,
            overwriteFiles: true
        );

        string applicationSource =
            FindApplicationRoot(
                applicationExtract
            );

        string packagedExe =
            Path.Combine(
                applicationSource,
                "slimevr.exe"
            );

        int copiedApplicationFiles =
            CopyDirectoryWithCount(
                applicationSource,
                targetDirectory
            );

        if (copiedApplicationFiles == 0)
        {
            throw new IOException(
                "Zero standalone application files were installed."
            );
        }

        progress?.Report(
            $"Installed {copiedApplicationFiles} standalone application files."
        );

        VerifyFileCopy(
            packagedExe,
            Path.Combine(
                targetDirectory,
                "slimevr.exe"
            ),
            "standalone slimevr.exe"
        );

        progress?.Report(
            "Standalone slimevr.exe replacement verified."
        );

        // The outer JAR is our custom server JAR and should win over
        // anything contained in the application ZIP.
        string jarDestination =
            FindStandaloneJarDestination(
                targetDirectory
            );

        progress?.Report(
            $"Installing standalone slimevr.jar to: {jarDestination}"
        );

        File.Copy(
            serverJar,
            jarDestination,
            overwrite: true
        );

        VerifyFileCopy(
            serverJar,
            jarDestination,
            "standalone slimevr.jar"
        );

        progress?.Report(
            "Standalone slimevr.jar replacement verified."
        );

        if (installEmbeddedDriver)
        {
            if (embeddedDriverZip == null)
            {
                throw new InvalidDataException(
                    "Driver installation was requested, but the release bundle does not contain an OpenVR driver ZIP."
                );
            }

            if (string.IsNullOrWhiteSpace(
                    driverTargetDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Driver installation was requested, but no OpenVR driver target directory was provided."
                );
            }

            progress?.Report(
                $"Installing embedded OpenVR driver to: {driverTargetDirectory}"
            );

            await ReplaceDriverFilesAsync(
                embeddedDriverZip,
                driverTargetDirectory
            );

            progress?.Report(
                "Embedded OpenVR driver installed."
            );
        }
    }

    public async Task ReplaceSteamReleaseFilesAsync(
        string archiveFilePath,
        string steamRootDirectory,
        IProgress<string>? progress = null)
    {
        ValidateArchive(
            archiveFilePath
        );

        if (string.IsNullOrWhiteSpace(
                steamRootDirectory))
        {
            throw new DirectoryNotFoundException(
                "No Steam SlimeVR installation directory was provided."
            );
        }

        archiveFilePath =
            Path.GetFullPath(
                archiveFilePath
            );

        steamRootDirectory =
            Path.GetFullPath(
                steamRootDirectory
            );

        if (!Directory.Exists(
                steamRootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Steam SlimeVR installation does not exist: {steamRootDirectory}"
            );
        }

        string applicationTarget =
            Path.Combine(
                steamRootDirectory,
                "x64",
                "SlimeVR"
            );

        string executableTarget =
            Path.Combine(
                applicationTarget,
                "slimevr.exe"
            );

        string jarTarget =
            Path.Combine(
                applicationTarget,
                "slimevr.jar"
            );

        string driverTarget =
            Path.Combine(
                steamRootDirectory,
                "bin",
                "win64"
            );

        if (!Directory.Exists(
                applicationTarget))
        {
            throw new DirectoryNotFoundException(
                "Steam SlimeVR application directory was not found:\n" +
                applicationTarget
            );
        }

        if (!File.Exists(
                executableTarget))
        {
            throw new FileNotFoundException(
                "Steam SlimeVR executable was not found.",
                executableTarget
            );
        }

        progress?.Report(
            "Stopping SlimeVR..."
        );

        StopRunningProcesses();

        progress?.Report(
            "Creating Steam SlimeVR backup..."
        );

        string backupDirectory =
            CreateBackup(
                steamRootDirectory
            );

        progress?.Report(
            $"Steam backup created: {backupDirectory}"
        );

        string tempExtract =
            CreateTemporaryDirectory(
                "SlimeVR_Steam_"
            );

        bool successful = false;

        try
        {
            progress?.Report(
                $"Extracting release bundle: {Path.GetFileName(archiveFilePath)}"
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

            string applicationZip =
                FindWindowsApplicationZip(
                    outerFiles
                );

            string serverJar =
                FindServerJar(
                    outerFiles
                );

            string? driverZip =
                FindDriverZip(
                    outerFiles
                );

            progress?.Report(
                $"Extracting Steam application package: {Path.GetFileName(applicationZip)}"
            );

            string applicationExtract =
                Path.Combine(
                    tempExtract,
                    "Steam_Application"
                );

            Directory.CreateDirectory(
                applicationExtract
            );

            ZipFile.ExtractToDirectory(
                applicationZip,
                applicationExtract,
                overwriteFiles: true
            );

            string applicationSource =
                FindApplicationRoot(
                    applicationExtract
                );

            string packagedExe =
                Path.Combine(
                    applicationSource,
                    "slimevr.exe"
                );

            progress?.Report(
                $"Installing Steam application to: {applicationTarget}"
            );

            int copiedApplicationFiles =
                CopyDirectoryWithCount(
                    applicationSource,
                    applicationTarget
                );

            if (copiedApplicationFiles == 0)
            {
                throw new IOException(
                    "Zero Steam application files were installed."
                );
            }

            progress?.Report(
                $"Installed {copiedApplicationFiles} Steam application files."
            );

            VerifyFileCopy(
                packagedExe,
                executableTarget,
                "Steam slimevr.exe"
            );

            progress?.Report(
                "Steam slimevr.exe replacement verified."
            );

            progress?.Report(
                $"Installing Steam slimevr.jar to: {jarTarget}"
            );

            File.Copy(
                serverJar,
                jarTarget,
                overwrite: true
            );

            VerifyFileCopy(
                serverJar,
                jarTarget,
                "Steam slimevr.jar"
            );

            progress?.Report(
                "Steam slimevr.jar replacement verified."
            );

            if (driverZip != null)
            {
                progress?.Report(
                    "Extracting Steam OpenVR driver..."
                );

                string driverExtract =
                    Path.Combine(
                        tempExtract,
                        "Steam_Driver"
                    );

                Directory.CreateDirectory(
                    driverExtract
                );

                ZipFile.ExtractToDirectory(
                    driverZip,
                    driverExtract,
                    overwriteFiles: true
                );

                string? packagedDriverDll =
                    Directory
                        .GetFiles(
                            driverExtract,
                            "driver_slimevr.dll",
                            SearchOption.AllDirectories
                        )
                        .FirstOrDefault();

                if (packagedDriverDll == null)
                {
                    throw new InvalidDataException(
                        "The OpenVR driver package does not contain driver_slimevr.dll."
                    );
                }

                Directory.CreateDirectory(
                    driverTarget
                );

                string installedDriverDll =
                    Path.Combine(
                        driverTarget,
                        "driver_slimevr.dll"
                    );

                progress?.Report(
                    $"Installing Steam driver DLL to: {installedDriverDll}"
                );

                File.Copy(
                    packagedDriverDll,
                    installedDriverDll,
                    overwrite: true
                );

                VerifyFileCopy(
                    packagedDriverDll,
                    installedDriverDll,
                    "Steam driver_slimevr.dll"
                );

                progress?.Report(
                    "Steam driver_slimevr.dll replacement verified."
                );
            }
            else
            {
                progress?.Report(
                    "Release bundle contains no OpenVR driver package; " +
                    "existing Steam driver was left unchanged."
                );
            }

            if (!File.Exists(
                    executableTarget))
            {
                throw new IOException(
                    $"Steam slimevr.exe is missing after installation: {executableTarget}"
                );
            }

            if (!File.Exists(
                    jarTarget))
            {
                throw new IOException(
                    $"Steam slimevr.jar is missing after installation: {jarTarget}"
                );
            }

            successful = true;

            progress?.Report(
                "Steam SlimeVR replacement completed successfully."
            );
        }
        catch (Exception ex)
        {
            progress?.Report(
                $"Steam update failed. Temporary extraction preserved at: {tempExtract}"
            );

            throw new Exception(
                "Steam SlimeVR replacement failed.\n\n" +
                $"Archive: {archiveFilePath}\n" +
                $"Steam root: {steamRootDirectory}\n" +
                $"Application: {applicationTarget}\n" +
                $"Driver: {driverTarget}\n" +
                $"Backup: {backupDirectory}\n" +
                $"Temporary extraction: {tempExtract}\n\n" +
                $"{ex.GetType().Name}: {ex.Message}",
                ex
            );
        }
        finally
        {
            if (successful)
            {
                TryDeleteDirectory(
                    tempExtract
                );
            }
        }
    }

    public async Task ReplaceDriverFilesAsync(
        string driverZipPath,
        string driverTargetDirectory)
    {
        ValidateArchive(
            driverZipPath
        );

        if (string.IsNullOrWhiteSpace(
                driverTargetDirectory))
        {
            throw new DirectoryNotFoundException(
                "No OpenVR driver target directory was provided."
            );
        }

        driverZipPath =
            Path.GetFullPath(
                driverZipPath
            );

        driverTargetDirectory =
            Path.GetFullPath(
                driverTargetDirectory
            );

        StopRunningProcesses();

        // Back up the existing driver if one exists.
        if (Directory.Exists(
                driverTargetDirectory))
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
            CreateTemporaryDirectory(
                "SlimeVR_Driver_"
            );

        bool successful = false;

        try
        {
            await ExtractArchiveAsync(
                driverZipPath,
                tempExtract
            );

            string driverRoot =
                GetArchiveContentRoot(
                    tempExtract
                );

            string[] extractedFiles =
                Directory.GetFiles(
                    driverRoot,
                    "*",
                    SearchOption.AllDirectories
                );

            if (extractedFiles.Length == 0)
            {
                throw new InvalidDataException(
                    "The OpenVR driver package extracted zero files."
                );
            }

            // If the ZIP has an extra wrapper directory, locate the
            // actual driver manifest and use its directory as root.
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
            if (successful)
            {
                TryDeleteDirectory(
                    tempExtract
                );
            }
        }
    }

    public async Task SelfUpdateAppAsync(
        string archiveFilePath)
    {
        ValidateArchive(
            archiveFilePath
        );

        string currentExePath =
            Process.GetCurrentProcess()
                .MainModule?.FileName
            ?? Environment.ProcessPath
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                currentExePath))
        {
            throw new Exception(
                "Could not determine current executable path."
            );
        }

        string currentDirectory =
            Path.GetDirectoryName(
                currentExePath
            )
            ?? throw new Exception(
                "Could not determine current application directory."
            );

        string tempExtract =
            CreateTemporaryDirectory(
                "SlimeVRExtender_SelfUpdate_"
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
                    "update_extender_" +
                    Guid.NewGuid().ToString("N") +
                    ".bat"
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
                    "update_extender_" +
                    Guid.NewGuid().ToString("N") +
                    ".sh"
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

    private static string FindWindowsApplicationZip(
        IEnumerable<string> files)
    {
        string? applicationZip =
            files.FirstOrDefault(file =>
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

        applicationZip ??=
            files.FirstOrDefault(file =>
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
                "The release bundle does not contain a SlimeVR-win*.zip application package."
            );
        }

        return applicationZip;
    }

    private static string FindServerJar(
        IEnumerable<string> files)
    {
        string? serverJar =
            files.FirstOrDefault(file =>
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

        return serverJar;
    }

    private static string? FindDriverZip(
        IEnumerable<string> files)
    {
        return files.FirstOrDefault(file =>
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
    }

    private static string FindApplicationRoot(
        string extractedDirectory)
    {
        string? executable =
            Directory
                .GetFiles(
                    extractedDirectory,
                    "slimevr.exe",
                    SearchOption.AllDirectories
                )
                .FirstOrDefault();

        if (executable == null)
        {
            throw new InvalidDataException(
                "The SlimeVR application package does not contain slimevr.exe."
            );
        }

        return Path.GetDirectoryName(
                   executable
               )
               ?? throw new InvalidDataException(
                   "Could not determine the SlimeVR application root."
               );
    }

    private static string FindStandaloneJarDestination(
        string targetDirectory)
    {
        string rootJar =
            Path.Combine(
                targetDirectory,
                "slimevr.jar"
            );

        if (File.Exists(rootJar))
        {
            return rootJar;
        }

        string[] existingJars =
            Directory.GetFiles(
                targetDirectory,
                "slimevr.jar",
                SearchOption.AllDirectories
            );

        if (existingJars.Length > 0)
        {
            return existingJars[0];
        }

        // Current standalone layout normally uses the root.
        return rootJar;
    }

    private static void PreserveConfiguration(
        string targetDirectory,
        IDictionary<string, byte[]> configBackup)
    {
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
                    File.ReadAllBytes(path);
            }
        }
    }

    private static async Task RestoreConfigurationAsync(
        string targetDirectory,
        IDictionary<string, byte[]> configBackup)
    {
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

    private static async Task ExtractArchiveAsync(
        string archiveFilePath,
        string destinationDirectory)
    {
        ValidateArchive(
            archiveFilePath
        );

        Directory.CreateDirectory(
            destinationDirectory
        );

        if (archiveFilePath.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            using (ZipArchive archive =
                   ZipFile.OpenRead(
                       archiveFilePath
                   ))
            {
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException(
                        $"ZIP archive contains no entries: {archiveFilePath}"
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
            $"Unsupported archive format: {Path.GetFileName(archiveFilePath)}"
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
        if (!Directory.Exists(
                sourceDirectory))
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

            if (!string.IsNullOrWhiteSpace(
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

    private static void VerifyFileCopy(
        string sourceFile,
        string destinationFile,
        string description)
    {
        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException(
                $"Source {description} does not exist.",
                sourceFile
            );
        }

        if (!File.Exists(destinationFile))
        {
            throw new IOException(
                $"{description} was not installed: {destinationFile}"
            );
        }

        FileInfo sourceInfo =
            new(sourceFile);

        FileInfo destinationInfo =
            new(destinationFile);

        if (sourceInfo.Length !=
            destinationInfo.Length)
        {
            throw new IOException(
                $"{description} verification failed.\n\n" +
                $"Source: {sourceInfo.Length:N0} bytes\n" +
                $"Installed: {destinationInfo.Length:N0} bytes"
            );
        }
    }

    private static void ValidateArchive(
        string archiveFilePath)
    {
        if (string.IsNullOrWhiteSpace(
                archiveFilePath))
        {
            throw new ArgumentException(
                "Archive path cannot be empty.",
                nameof(archiveFilePath)
            );
        }

        if (!File.Exists(
                archiveFilePath))
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
    }

    private static string CreateTemporaryDirectory(
        string prefix)
    {
        string path =
            Path.Combine(
                Path.GetTempPath(),
                prefix +
                Guid.NewGuid().ToString("N")
            );

        Directory.CreateDirectory(
            path
        );

        return path;
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    true
                );
            }
        }
        catch
        {
            // Cleanup failure should not invalidate a successful update.
        }
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