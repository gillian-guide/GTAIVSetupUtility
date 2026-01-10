using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using GTAIVSetupUtility.Service;
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
            if (File.Exists("GTAIVSetupUtilityLog.txt")) { File.Delete("GTAIVSetupUtilityLog.txt"); }
            LogManager.Setup().LoadConfiguration(builder => {
                builder.ForLogger().FilterMinLevel(NLog.LogLevel.Debug).WriteToConsole();
                builder.ForLogger().FilterMinLevel(NLog.LogLevel.Info).WriteToFile(fileName: "GTAIVSetupUtilityLog.txt");
            });
            
            Logger.Info(" Initializing the application...");
            
            DisableAvaloniaDataAnnotationValidation();
            
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.MainWindow = mainWindow;
            
            mainWindow.Show();
            
            Logger.Info(" Checking for Linux running under Wine...");
            
            if (OsDetectionService.IsLinux())
            {
                Logger.Info(" Detected Linux - DXVK functionality will be disabled.");
                
                viewModel.InstallDxvk = 0; 
                viewModel.IsDxvkPanelEnabled = false;

                var box = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(
                    "Linux Detected - DXVK Unavailable",
                    "DXVK installation and Vulkan checking is only available on Windows.\n\n" +
                    "These features have been disabled. Launch options configuration remains available.",
                    MsBox.Avalonia.Enums.ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info);
                
                await box.ShowWindowDialogAsync(mainWindow);
            }
            else
            {
                Logger.Info(" Application is not running under Wine.");
                
                Logger.Info(" Initializing the vulkan check...");
                var vulkanInfo = VulkanCheckerService.VulkanCheck();
                await viewModel.ReceiveVulkanInfoAsync(vulkanInfo);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}