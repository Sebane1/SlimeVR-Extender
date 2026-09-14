using Avalonia;
using Avalonia.ReactiveUI;
using Spectre.Console;
using SlimeVRExtender.Services;

namespace SlimeVRExtender;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && (args.Contains("--cli") || args.Contains("-h") || args.Contains("--help") || args.Contains("--target")))
        {
            RunCliModeAsync(args).GetAwaiter().GetResult();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GUI Initialization failed: {ex.Message}");
            Console.WriteLine("Falling back to CLI mode...");
            RunCliModeAsync(args).GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();

    private static async Task RunCliModeAsync(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("SlimeVR Extender")
                .Color(Color.DeepSkyBlue1));

        var platformService = new PlatformService();
        var pathResolver = new PathResolver(platformService);
        var releaseService = new ReleaseService(platformService);
        var replacerEngine = new ReplacerEngine(platformService);

        AnsiConsole.MarkupLine($"[bold gray]Detected OS:[/] [cyan]{platformService.GetPlatformDisplayName()}[/]");
        AnsiConsole.MarkupLine($"[bold gray]Target Driver Zip:[/] [yellow]{platformService.GetDriverZipName()}[/]");
        AnsiConsole.WriteLine();

        string repo = GetArgValue(args, "--repo") ?? "Sebane1/SlimeVR-Server";
        bool updateStandalone = !args.Contains("--skip-standalone");
        bool updateSteam = args.Contains("--update-steam");
        bool updateDriver = !args.Contains("--skip-driver");

        string standalonePath = GetArgValue(args, "--standalone-path") ?? pathResolver.DetectStandaloneSlimeVRPath();
        string steamPath = GetArgValue(args, "--steam-path") ?? pathResolver.DetectSteamSlimeVRPath();
        string driverPath = GetArgValue(args, "--driver-path") ?? pathResolver.DetectSteamVRDriverPath();

        AnsiConsole.MarkupLine($"[bold blue]Configured Paths:[/]");
        AnsiConsole.MarkupLine($"  • Standalone: [dim]{standalonePath}[/]");
        AnsiConsole.MarkupLine($"  • Steam App:  [dim]{steamPath}[/]");
        AnsiConsole.MarkupLine($"  • Driver Path:[dim]{driverPath}[/]");
        AnsiConsole.WriteLine();

        await AnsiConsole.Status()
            .StartAsync("Fetching release from GitHub...", async ctx =>
            {
                var release = await releaseService.FetchLatestReleaseAsync(repo);
                if (release == null)
                {
                    AnsiConsole.MarkupLine("[bold red]Failed to fetch release from GitHub.[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"[bold green]Release found:[/] {release.tag_name}");

                string tempDir = Path.Combine(Path.GetTempPath(), "SlimeVR_CLI_Downloads");
                Directory.CreateDirectory(tempDir);

                var platformAsset = releaseService.GetMatchingPlatformAsset(release);
                var driverAsset = releaseService.GetMatchingDriverAsset(release);

                string releasePackageFile = string.Empty;
                if ((updateStandalone || updateSteam) && platformAsset != null)
                {
                    ctx.Status($"Downloading {platformAsset.name}...");
                    releasePackageFile = Path.Combine(tempDir, platformAsset.name);
                    await releaseService.DownloadFileAsync(platformAsset.browser_download_url, releasePackageFile);
                }

                string driverPackageFile = string.Empty;
                if (updateDriver)
                {
                    string driverZipName = platformService.GetDriverZipName();
                    if (driverAsset != null)
                    {
                        ctx.Status($"Downloading Driver {driverAsset.name}...");
                        driverPackageFile = Path.Combine(tempDir, driverAsset.name);
                        await releaseService.DownloadFileAsync(driverAsset.browser_download_url, driverPackageFile);
                    }
                    else if (!string.IsNullOrEmpty(driverZipName))
                    {
                        string nightlyUrl = $"https://nightly.link/SlimeVR/SlimeVR-OpenVR-Driver/workflows/c-cpp/sapphire%2Fsolarxr-external-trackers-rewrite/{driverZipName}";
                        ctx.Status($"Downloading Driver from Nightly Link...");
                        driverPackageFile = Path.Combine(tempDir, driverZipName);
                        await releaseService.DownloadFileAsync(nightlyUrl, driverPackageFile);
                    }
                }

                ctx.Status("Replacing files...");

                if (updateStandalone && !string.IsNullOrEmpty(releasePackageFile) && File.Exists(releasePackageFile))
                {
                    AnsiConsole.MarkupLine("[yellow]Replacing Standalone SlimeVR files...[/]");
                    await replacerEngine.ReplaceReleaseFilesAsync(releasePackageFile, standalonePath, true);
                }

                if (updateSteam && !string.IsNullOrEmpty(releasePackageFile) && File.Exists(releasePackageFile) && !string.IsNullOrEmpty(steamPath))
                {
                    AnsiConsole.MarkupLine("[yellow]Replacing Steam SlimeVR files...[/]");
                    await replacerEngine.ReplaceReleaseFilesAsync(releasePackageFile, steamPath, true);
                }

                if (updateDriver && !string.IsNullOrEmpty(driverPackageFile) && File.Exists(driverPackageFile) && !string.IsNullOrEmpty(driverPath))
                {
                    AnsiConsole.MarkupLine("[yellow]Replacing SteamVR SlimeVR Driver files...[/]");
                    await replacerEngine.ReplaceDriverFilesAsync(driverPackageFile, driverPath);
                }

                AnsiConsole.MarkupLine("[bold green]Successfully updated all selected SlimeVR targets![/]");
            });
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
