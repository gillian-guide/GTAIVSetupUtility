using Avalonia;

namespace GTAIVSetupUtility;

sealed class Program
{
    /// <summary>
    /// Optional automation hook (used with a DEBUG marker during development/UI checks).
    /// </summary>
    public static string? StartupGameDirectory { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--game-dir", StringComparison.OrdinalIgnoreCase))
            StartupGameDirectory = args[1];

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
