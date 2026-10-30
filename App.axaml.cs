using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GTAIVSetupUtility.Services;
using GTAIVSetupUtility.ViewModels;
using GTAIVSetupUtility.Views;
using NLog;

namespace GTAIVSetupUtility;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool debug = File.Exists("DEBUG");
            var loglevel = debug ? LogLevel.Debug : LogLevel.Info;
            Localizations.Resources.Culture = debug ? new CultureInfo(CultureInfo.CurrentUICulture.Name) : new CultureInfo("en-US");

            if (File.Exists("GTAIVSetupUtilityLog.txt")) { File.Delete("GTAIVSetupUtilityLog.txt"); }
            LogManager.Setup().LoadConfiguration(builder =>
            {
                builder.ForLogger().FilterMinLevel(NLog.LogLevel.Debug).WriteToConsole();
                builder.ForLogger().FilterMinLevel(loglevel).WriteToFile(fileName: "GTAIVSetupUtilityLog.txt");
            });

            Logger.Info(" Initializing the application...");

            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.MainWindow = mainWindow;

            mainWindow.Show();

            Logger.Info(" Checking for Linux running under Wine...");

            bool isLinux = OsDetectionService.IsLinux();
            if (isLinux)
            {
                // DXVK installation is pointless under Wine/Proton (the host provides it), but
                // DLSS-IV still runs through that DXVK, so the hardware probe is still useful.
                Logger.Info(" Detected Linux - DXVK installation will be disabled.");

                viewModel.InstallDxvk = 0;
                viewModel.IsDxvkPanelEnabled = false;
                viewModel.IsLinux = true;

                await MessageBoxService.ShowAsync(
                    Localizations.Resources.LinuxDetectedTitle,
                    Localizations.Resources.LinuxDetectedDescription,
                    MsBox.Avalonia.Enums.Icon.Info);
            }
            else
            {
                Logger.Info(" Application is not running under Wine.");
            }

            Logger.Info(" Initializing the vulkan check...");
            var vulkanInfo = await VulkanCheckerService.VulkanCheckAsync(showNotifications: !isLinux);

            if (isLinux)
                await viewModel.ReceiveHardwareInfoAsync(vulkanInfo);
            else
                await viewModel.ReceiveVulkanInfoAsync(vulkanInfo);

            if (File.Exists("DEBUG") && !string.IsNullOrWhiteSpace(Program.StartupGameDirectory))
            {
                Logger.Info($" DEBUG game folder override: {Program.StartupGameDirectory}");
                await viewModel.OpenGameDirectoryAsync(Program.StartupGameDirectory);
            }
            else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "GTAIV.exe")))
            {
                Logger.Info($" GTAIV.exe found next to the utility - opening {AppContext.BaseDirectory}");
                await viewModel.OpenGameDirectoryAsync(AppContext.BaseDirectory);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
