using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using GTAIVSetupUtility.Localizations;
using MsBox.Avalonia;
using MsBox.Avalonia.Base;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using WindowStartupLocation = Avalonia.Controls.WindowStartupLocation;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// Single entry point for modal dialogs. Every message box gets the same owner, sizing and
/// newline handling instead of each caller repeating the parameter object.
/// </summary>
public static class MessageBoxService
{
    public static async Task<ButtonResult> ShowAsync(
        string title,
        string message,
        Icon icon = Icon.Info,
        ButtonEnum buttons = ButtonEnum.Ok)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = title,
            ContentMessage = Normalize(message),
            ButtonDefinitions = buttons,
            Icon = icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            MaxWidth = 600,
            ShowInCenter = true
        });

        return await ShowAsync(box);
    }

    public static async Task<bool> ConfirmAsync(string title, string message)
        => await ShowAsync(title, message, Icon.Question, ButtonEnum.YesNo) == ButtonResult.Yes;

    public static Task ShowErrorAsync(string title, string message)
        => ShowAsync(title, message, Icon.Error);

    /// <summary>
    /// Shows a small text prompt. Returns <see langword="null"/> when there is no main window
    /// to own the dialog, or when the user closes it without confirming.
    /// </summary>
    public static async Task<string?> ShowInputAsync(string title, string message, string? initialValue = null)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } owner
            })
        {
            return null;
        }

        var window = new Window
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur],
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight
        };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        var label = new TextBlock
        {
            Text = Normalize(message),
            TextWrapping = TextWrapping.Wrap
        };
        var textBox = new TextBox { Text = initialValue ?? string.Empty };
        var okButton = new Button
        {
            Content = Resources.TextOk,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };

        okButton.Click += (_, _) => window.Close(textBox.Text);

        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(okButton);
        window.Content = panel;

        return await window.ShowDialog<string>(owner);
    }

    private static async Task<ButtonResult> ShowAsync(IMsBox<ButtonResult> box)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } owner
            })
        {
            return await box.ShowWindowDialogAsync(owner);
        }

        return await box.ShowAsync();
    }

    private static string Normalize(string message) => message.Replace("\\n", "\n", StringComparison.Ordinal);
}
