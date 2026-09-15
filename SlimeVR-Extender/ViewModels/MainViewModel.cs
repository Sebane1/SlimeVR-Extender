using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SlimeVRExtender.Services;

namespace SlimeVRExtender.ViewModels;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting &&
               (_canExecute == null || _canExecute());
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    public event EventHandler? CanExecuteChanged;
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly PlatformService _platformService;
    private readonly PathResolver _pathResolver;
    private readonly ReleaseService _releaseService;
    private readonly ReplacerEngine _replacerEngine;

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _platformName = string.Empty;

    public string PlatformName
    {
        get => _platformName;
        set => SetField(ref _platformName, value);
    }

    private string _repository =
        "Sebane1/SlimeVR-Extender";

    public string Repository
    {
        get => _repository;
        set => SetField(ref _repository, value);
    }

    public string[] InstallationTypes { get; } =
    [
        "Standalone",
        "Steam"
    ];

    private string _selectedInstallationType =
        "Standalone";

    public string SelectedInstallationType
    {
        get => _selectedInstallationType;

        set
        {
            if (!SetField(
                    ref _selectedInstallationType,
                    value))
            {
                return;
            }

            RefreshSelectedInstallation();
        }
    }

    public bool IsStandalone =>
        SelectedInstallationType == "Standalone";

    public bool IsSteam =>
        SelectedInstallationType == "Steam";

    private string _standalonePath = string.Empty;

    public string StandalonePath
    {
        get => _standalonePath;

        set
        {
            if (!SetField(ref _standalonePath, value))
                return;

            if (IsStandalone)
                RefreshDriverPath();
        }
    }

    private string _steamPath = string.Empty;

    public string SteamPath
    {
        get => _steamPath;

        set
        {
            if (!SetField(ref _steamPath, value))
                return;

            if (IsSteam)
                RefreshDriverPath();
        }
    }

    private string _driverPath = string.Empty;

    public string DriverPath
    {
        get => _driverPath;
        set => SetField(ref _driverPath, value);
    }

    public string SelectedInstallationPath =>
        IsStandalone
            ? StandalonePath
            : SteamPath;

    private bool _updateDriver = true;

    public bool UpdateDriver
    {
        get => _updateDriver;
        set => SetField(ref _updateDriver, value);
    }

    private bool _updateExtenderApp = false;

    public bool UpdateExtenderApp
    {
        get => _updateExtenderApp;
        set => SetField(ref _updateExtenderApp, value);
    }

    private string _statusText =
        "Ready to fetch latest release.";

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private double _progressValue;

    public double ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand FetchLatestCommand { get; }
    public ICommand UpdateCommand { get; }

    public MainViewModel()
    {
        _platformService =
            new PlatformService();

        _pathResolver =
            new PathResolver(_platformService);

        _releaseService =
            new ReleaseService(_platformService);

        _replacerEngine =
            new ReplacerEngine(_platformService);

        PlatformName =
            _platformService.GetPlatformDisplayName();

        _standalonePath =
            _pathResolver.DetectStandaloneSlimeVRPath();

        _steamPath =
            _pathResolver.DetectSteamSlimeVRPath();

        RefreshSelectedInstallation();

        FetchLatestCommand =
            new AsyncRelayCommand(
                FetchReleaseInfoAsync
            );

        UpdateCommand =
            new AsyncRelayCommand(
                ExecuteUpdateAsync
            );
    }

    private void RefreshSelectedInstallation()
    {
        OnPropertyChanged(nameof(IsStandalone));
        OnPropertyChanged(nameof(IsSteam));
        OnPropertyChanged(nameof(SelectedInstallationPath));

        RefreshDriverPath();
    }

    private void RefreshDriverPath()
    {
        if (IsStandalone)
        {
            DriverPath =
                _pathResolver.DetectSteamVRDriverPath();
        }
        else
        {
            DriverPath =
                string.IsNullOrWhiteSpace(SteamPath)
                    ? string.Empty
                    : Path.Combine(
                        SteamPath,
                        "bin",
                        "win64"
                    );
        }

        OnPropertyChanged(nameof(SelectedInstallationPath));
    }

    public async Task FetchReleaseInfoAsync()
    {
        IsBusy = true;

        StatusText =
            "Fetching release metadata from GitHub...";

        try
        {
            var release =
                await _releaseService
                    .FetchLatestReleaseAsync(
                        Repository
                    );

            if (release == null)
            {
                StatusText =
                    "Failed to fetch release. Verify repository path.";

                return;
            }

            var asset =
                _releaseService
                    .GetMatchingPlatformAsset(
                        release
                    );

            var driverAsset =
                _releaseService
                    .GetMatchingDriverAsset(
                        release
                    );

            string assetName =
                asset?.name ??
                "No matching OS package";

            string driverName =
                driverAsset?.name ??
                _platformService
                    .GetDriverZipName();

            StatusText =
                $"Found {release.tag_name} | " +
                $"Asset: {assetName} | " +
                $"Driver: {driverName}";
        }
        catch (Exception ex)
        {
            StatusText =
                $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteUpdateAsync()
    {
        IsBusy = true;
        ProgressValue = 0;

        StatusText =
            "Connecting to GitHub Releases...";

        try
        {
     
            if (IsStandalone)
            {
                if (string.IsNullOrWhiteSpace(
                        StandalonePath))
                {
                    StandalonePath =
                        _pathResolver
                            .DetectStandaloneSlimeVRPath();
                }

                if (string.IsNullOrWhiteSpace(
                        StandalonePath))
                {
                    throw new DirectoryNotFoundException(
                        "Could not locate the standalone SlimeVR installation."
                    );
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(
                        SteamPath))
                {
                    SteamPath =
                        _pathResolver
                            .DetectSteamSlimeVRPath();
                }

                if (string.IsNullOrWhiteSpace(
                        SteamPath))
                {
                    throw new DirectoryNotFoundException(
                        "Could not locate the Steam SlimeVR installation."
                    );
                }
            }

            var release =
                await _releaseService
                    .FetchLatestReleaseAsync(
                        Repository
                    );

            if (release == null)
            {
                throw new InvalidOperationException(
                    "Could not retrieve release details from GitHub."
                );
            }

            string tempDir =
                Path.Combine(
                    Path.GetTempPath(),
                    "SlimeVR_Extender_Downloads"
                );

            Directory.CreateDirectory(tempDir);

            var platformAsset =
                _releaseService
                    .GetMatchingPlatformAsset(
                        release
                    );

            if (platformAsset == null)
            {
                throw new InvalidOperationException(
                    $"No SlimeVR server package was found for {PlatformName}."
                );
            }

            var driverAsset =
                _releaseService
                    .GetMatchingDriverAsset(
                        release
                    );

            StatusText =
                $"Downloading {platformAsset.name}...";

            string releasePackageFile =
                Path.Combine(
                    tempDir,
                    platformAsset.name
                );

            var releaseProgress =
                new Progress<double>(
                    p => ProgressValue = p * 0.6
                );

            await _releaseService
                .DownloadFileAsync(
                    platformAsset.browser_download_url,
                    releasePackageFile,
                    releaseProgress
                );

            if (!File.Exists(releasePackageFile))
            {
                throw new FileNotFoundException(
                    "The downloaded SlimeVR server package could not be found.",
                    releasePackageFile
                );
            }

            string driverPackageFile =
                string.Empty;

            if (UpdateDriver && IsStandalone)
            {
                string driverZipName =
                    _platformService
                        .GetDriverZipName();

                if (driverAsset != null)
                {
                    StatusText =
                        $"Downloading OpenVR Driver ({driverAsset.name})...";

                    driverPackageFile =
                        Path.Combine(
                            tempDir,
                            driverAsset.name
                        );

                    var driverProgress =
                        new Progress<double>(
                            p =>
                                ProgressValue =
                                    60 + (p * 0.2)
                        );

                    await _releaseService
                        .DownloadFileAsync(
                            driverAsset.browser_download_url,
                            driverPackageFile,
                            driverProgress
                        );
                }
                else if (!string.IsNullOrEmpty(
                             driverZipName))
                {
                    string nightlyUrl =
                        $"https://nightly.link/SlimeVR/SlimeVR-OpenVR-Driver/workflows/c-cpp/sapphire%2Fsolarxr-external-trackers-rewrite/{driverZipName}";

                    StatusText =
                        $"Downloading OpenVR Driver from Nightly Link ({driverZipName})...";

                    driverPackageFile =
                        Path.Combine(
                            tempDir,
                            driverZipName
                        );

                    var driverProgress =
                        new Progress<double>(
                            p =>
                                ProgressValue =
                                    60 + (p * 0.2)
                        );

                    await _releaseService
                        .DownloadFileAsync(
                            nightlyUrl,
                            driverPackageFile,
                            driverProgress
                        );
                }
            }

            var replacementProgress =
                new Progress<string>(
                    message =>
                        StatusText = message
                );

            if (IsStandalone)
            {
                StatusText =
                    "Updating Standalone SlimeVR...";

                await _replacerEngine
                    .ReplaceReleaseFilesAsync(
                        releasePackageFile,
                        StandalonePath,
                        true,
                        replacementProgress
                    );

                if (UpdateDriver)
                {
                    if (string.IsNullOrWhiteSpace(
                            DriverPath))
                    {
                        DriverPath =
                            _pathResolver
                                .DetectSteamVRDriverPath();
                    }

                    if (string.IsNullOrWhiteSpace(
                            DriverPath))
                    {
                        throw new DirectoryNotFoundException(
                            "Could not locate the SteamVR driver installation path."
                        );
                    }

                    if (string.IsNullOrWhiteSpace(
                            driverPackageFile) ||
                        !File.Exists(
                            driverPackageFile))
                    {
                        throw new FileNotFoundException(
                            "The OpenVR driver package could not be downloaded."
                        );
                    }

                    StatusText =
                        "Updating SteamVR SlimeVR Driver...";

                    await _replacerEngine
                        .ReplaceDriverFilesAsync(
                            driverPackageFile,
                            DriverPath
                        );
                }
            }
            else
            {
                StatusText =
                    "Updating Steam SlimeVR...";

                await _replacerEngine
                    .ReplaceSteamReleaseFilesAsync(
                        releasePackageFile,
                        SteamPath,
                        replacementProgress
                    );
            }

            if (UpdateExtenderApp)
            {
                var extenderAsset =
                    _releaseService
                        .GetMatchingExtenderAppAsset(
                            release
                        );

                if (extenderAsset != null)
                {
                    StatusText =
                        "Downloading SlimeVR Extender update...";

                    string extenderZipFile =
                        Path.Combine(
                            tempDir,
                            extenderAsset.name
                        );

                    var extenderProgress =
                        new Progress<double>(
                            p =>
                                ProgressValue =
                                    80 + (p * 0.2)
                        );

                    await _releaseService
                        .DownloadFileAsync(
                            extenderAsset.browser_download_url,
                            extenderZipFile,
                            extenderProgress
                        );

                    StatusText =
                        "Relaunching updated SlimeVR Extender...";

                    await _replacerEngine
                        .SelfUpdateAppAsync(
                            extenderZipFile
                        );

                    return;
                }
            }

            await Task.Yield();

            ProgressValue = 100;

            StatusText =
                IsStandalone
                    ? "SUCCESS! Standalone SlimeVR was updated successfully."
                    : "SUCCESS! Steam SlimeVR was updated successfully.";
        }
        catch (Exception ex)
        {
            StatusText =
                $"Update Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName]
        string? propertyName = null)
    {
        if (EqualityComparer<T>
            .Default
            .Equals(field, value))
        {
            return false;
        }

        field = value;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName
            )
        );

        return true;
    }

    protected void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName
            )
        );
    }
}