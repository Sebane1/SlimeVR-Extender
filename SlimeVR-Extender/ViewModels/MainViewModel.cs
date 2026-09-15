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

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute == null || _canExecute());

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
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

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

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

    private string _repository = "Sebane1/SlimeVR-Extender";
    public string Repository
    {
        get => _repository;
        set => SetField(ref _repository, value);
    }

    private string _standalonePath = string.Empty;
    public string StandalonePath
    {
        get => _standalonePath;
        set => SetField(ref _standalonePath, value);
    }

    private string _steamPath = string.Empty;
    public string SteamPath
    {
        get => _steamPath;
        set => SetField(ref _steamPath, value);
    }

    private string _driverPath = string.Empty;
    public string DriverPath
    {
        get => _driverPath;
        set => SetField(ref _driverPath, value);
    }

    private bool _updateStandalone = true;
    public bool UpdateStandalone
    {
        get => _updateStandalone;
        set => SetField(ref _updateStandalone, value);
    }

    private bool _updateSteam = false;
    public bool UpdateSteam
    {
        get => _updateSteam;
        set => SetField(ref _updateSteam, value);
    }

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

    private string _statusText = "Ready to fetch latest release.";
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private double _progressValue = 0;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    private bool _isBusy = false;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand FetchLatestCommand { get; }
    public ICommand UpdateCommand { get; }

    public MainViewModel()
    {
        _platformService = new PlatformService();
        _pathResolver = new PathResolver(_platformService);
        _releaseService = new ReleaseService(_platformService);
        _replacerEngine = new ReplacerEngine(_platformService);

        PlatformName = _platformService.GetPlatformDisplayName();
        StandalonePath = _pathResolver.DetectStandaloneSlimeVRPath();
        SteamPath = _pathResolver.DetectSteamSlimeVRPath();
        DriverPath = _pathResolver.DetectSteamVRDriverPath();

        FetchLatestCommand = new AsyncRelayCommand(FetchReleaseInfoAsync);
        UpdateCommand = new AsyncRelayCommand(ExecuteUpdateAsync);
    }

    public async Task FetchReleaseInfoAsync()
    {
        IsBusy = true;
        StatusText = "Fetching release metadata from GitHub...";
        try
        {
            var release = await _releaseService.FetchLatestReleaseAsync(Repository);
            if (release == null)
            {
                StatusText = "Failed to fetch release. Verify repository path.";
                return;
            }

            var asset = _releaseService.GetMatchingPlatformAsset(release);
            var driverAsset = _releaseService.GetMatchingDriverAsset(release);

            string assetName = asset?.name ?? "No matching OS package";
            string driverName = driverAsset?.name ?? _platformService.GetDriverZipName();
            StatusText = $"Found {release.tag_name} | Asset: {assetName} | Driver: {driverName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteUpdateAsync()
    {
        if (!UpdateStandalone && !UpdateSteam && !UpdateDriver)
        {
            StatusText = "Please select at least one component to update (Standalone, Steam, or Driver).";
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        StatusText = "Connecting to GitHub Releases...";

        try
        {
            var release = await _releaseService.FetchLatestReleaseAsync(Repository);
            if (release == null)
            {
                StatusText = "Error: Could not retrieve release details from GitHub.";
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "SlimeVR_Extender_Downloads");
            Directory.CreateDirectory(tempDir);

            var platformAsset = _releaseService.GetMatchingPlatformAsset(release);
            var driverAsset = _releaseService.GetMatchingDriverAsset(release);

            // Download Platform Release if Standalone or Steam selected
            string releasePackageFile = string.Empty;
            if ((UpdateStandalone || UpdateSteam) && platformAsset != null)
            {
                StatusText = $"Downloading {platformAsset.name}...";
                releasePackageFile = Path.Combine(tempDir, platformAsset.name);
                var progress = new Progress<double>(p => ProgressValue = p * 0.5);
                await _releaseService.DownloadFileAsync(platformAsset.browser_download_url, releasePackageFile, progress);
            }

            // Download Driver if selected and separate driver asset is available
            string driverPackageFile = string.Empty;
            if (UpdateDriver)
            {
                string driverZipName = _platformService.GetDriverZipName();
                if (driverAsset != null)
                {
                    StatusText = $"Downloading OpenVR Driver ({driverAsset.name})...";
                    driverPackageFile = Path.Combine(tempDir, driverAsset.name);
                    var progress = new Progress<double>(p => ProgressValue = 50 + (p * 0.5));
                    await _releaseService.DownloadFileAsync(driverAsset.browser_download_url, driverPackageFile, progress);
                }
                else if (!string.IsNullOrEmpty(driverZipName))
                {
                    // Direct nightly link fallback
                    string nightlyUrl = $"https://nightly.link/SlimeVR/SlimeVR-OpenVR-Driver/workflows/c-cpp/sapphire%2Fsolarxr-external-trackers-rewrite/{driverZipName}";
                    StatusText = $"Downloading OpenVR Driver from Nightly Link ({driverZipName})...";
                    driverPackageFile = Path.Combine(tempDir, driverZipName);
                    var progress = new Progress<double>(p => ProgressValue = 50 + (p * 0.5));
                    await _releaseService.DownloadFileAsync(nightlyUrl, driverPackageFile, progress);
                }
            }

            StatusText = "Applying file replacements & backups...";

            // Apply Standalone update
            if (UpdateStandalone && !string.IsNullOrEmpty(releasePackageFile) && File.Exists(releasePackageFile))
            {
                if (string.IsNullOrEmpty(StandalonePath))
                    StandalonePath = _pathResolver.DetectStandaloneSlimeVRPath();

                StatusText = "Updating Standalone SlimeVR...";

                var replacementProgress = new Progress<string>(message =>
                {
                    StatusText = message;
                });

                await _replacerEngine.ReplaceReleaseFilesAsync(
                    releasePackageFile,
                    StandalonePath,
                    true,
                    replacementProgress);
            }

            // Apply Steam update
            if (UpdateSteam && !string.IsNullOrEmpty(releasePackageFile) && File.Exists(releasePackageFile))
            {
                if (string.IsNullOrEmpty(SteamPath))
                    SteamPath = _pathResolver.DetectSteamSlimeVRPath();

                if (!string.IsNullOrEmpty(SteamPath))
                {
                    StatusText = "Updating Steam SlimeVR Installation...";
                    await _replacerEngine.ReplaceReleaseFilesAsync(releasePackageFile, SteamPath, true);
                }
                else
                {
                    StatusText = "Warning: Steam SlimeVR path could not be detected automatically.";
                }
            }

            // Apply SteamVR Driver update
            if (UpdateDriver && !string.IsNullOrEmpty(driverPackageFile) && File.Exists(driverPackageFile))
            {
                if (string.IsNullOrEmpty(DriverPath))
                    DriverPath = _pathResolver.DetectSteamVRDriverPath();

                if (!string.IsNullOrEmpty(DriverPath))
                {
                    StatusText = "Updating SteamVR SlimeVR Driver...";
                    await _replacerEngine.ReplaceDriverFilesAsync(driverPackageFile, DriverPath);
                }
                else
                {
                    StatusText = "Warning: SteamVR driver path could not be located.";
                }
            }

            // Self-Update SlimeVR Extender App
            if (UpdateExtenderApp)
            {
                var extenderAsset = _releaseService.GetMatchingExtenderAppAsset(release);
                if (extenderAsset != null)
                {
                    StatusText = "Downloading SlimeVR Extender update...";
                    string extenderZipFile = Path.Combine(tempDir, extenderAsset.name);
                    var progress = new Progress<double>(p => ProgressValue = p);
                    await _releaseService.DownloadFileAsync(extenderAsset.browser_download_url, extenderZipFile, progress);

                    StatusText = "Relaunching updated SlimeVR Extender...";
                    await _replacerEngine.SelfUpdateAppAsync(extenderZipFile);
                    return;
                }
            }

            ProgressValue = 100;
            StatusText = "SUCCESS! All selected components have been replaced and updated successfully. You can close the this and run SlimeVR Server";
        }
        catch (Exception ex)
        {
            StatusText = $"Update Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
